using System.Data.Common;
using Microsoft.Data.SqlClient;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace ViShap.Rewind.Integration.Tests.Fixtures;

/// <summary>A database in a container: started once for its test collection, removed after it.</summary>
public abstract class DatabaseFixture : IAsyncLifetime
{
    private int _nextKey;

    /// <summary>The provider's name, for test output.</summary>
    public abstract string Provider { get; }

    /// <summary>A key no other test has used, so tests never see one another's rows.</summary>
    public int NextKey() => Interlocked.Increment(ref _nextKey);

    /// <summary>
    /// Opens a connection. With <paramref name="enlist"/> it joins the ambient transaction, as application code does;
    /// without it, it reads committed data from outside any transaction, as a check does.
    /// </summary>
    public abstract DbConnection Open(bool enlist = true);

    /// <summary>The SQL error a duplicate primary key raises, as the provider reports it.</summary>
    public abstract bool IsDuplicateKey(Exception exception);

    public abstract Task InitializeAsync();

    public abstract Task DisposeAsync();

    /// <summary>Creates the table every scenario writes to.</summary>
    protected void CreateSchema(string sql)
    {
        using var connection = Open(enlist: false);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public int Count(int key)
    {
        using var connection = Open(enlist: false);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM orders WHERE id = {key}";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void Insert(int key, string status)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO orders (id, status) VALUES ({key}, '{status}')";
        command.ExecuteNonQuery();
    }

    public async Task InsertAsync(int key, string status)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO orders (id, status) VALUES ({key}, '{status}')";
        await command.ExecuteNonQueryAsync();
    }
}

public sealed class PostgresFixture : DatabaseFixture
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

    public override string Provider => "PostgreSQL 16 (Npgsql)";

    public string ConnectionString { get; private set; } = string.Empty;

    public override DbConnection Open(bool enlist = true)
    {
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(ConnectionString) { Enlist = enlist }.ConnectionString);
        connection.Open();
        return connection;
    }

    public override bool IsDuplicateKey(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    public override async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
        CreateSchema(
            "CREATE TABLE orders (id int PRIMARY KEY, status text NOT NULL);" +
            // A uniqueness checked only at COMMIT: the way to make the database itself refuse the commit.
            "CREATE TABLE ledger (entry int NOT NULL, CONSTRAINT ledger_entry UNIQUE (entry) DEFERRABLE INITIALLY DEFERRED);");
    }

    public override Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

public sealed class SqlServerFixture : DatabaseFixture
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    private string _connectionString = string.Empty;

    public override string Provider => "SQL Server 2022 (Microsoft.Data.SqlClient)";

    public override DbConnection Open(bool enlist = true)
    {
        var connection = new SqlConnection(new SqlConnectionStringBuilder(_connectionString) { Enlist = enlist, TrustServerCertificate = true }.ConnectionString);
        connection.Open();
        return connection;
    }

    public override bool IsDuplicateKey(Exception exception) => exception is SqlException { Number: 2627 or 2601 };

    public override async Task InitializeAsync()
    {
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();
        CreateSchema("CREATE TABLE orders (id int PRIMARY KEY, status nvarchar(50) NOT NULL);");
    }

    public override Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL";
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SQL Server";
}

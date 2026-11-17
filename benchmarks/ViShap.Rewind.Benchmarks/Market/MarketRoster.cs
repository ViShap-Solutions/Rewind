using ViShap.Rewind.Benchmarks.Market.Adapters;

namespace ViShap.Rewind.Benchmarks.Market;

/// <summary>
/// The roster of Track B in its fixed order — the floor, Rewind, then the alternatives — and the libraries the plan
/// names that are not measured, each with why. Rows in every table keep this order and are never sorted by a number.
/// </summary>
internal static class MarketRoster
{
    private static readonly Func<MarketAdapter>[] Factories =
    [
        () => new FloorAdapter(),
        () => new RewindAdapter(),
        () => new MementoAdapter(),
        () => new ImmutableAdapter(),
        () => new ShieldedAdapter(),
        () => new StmSharpAdapter(),
        () => new LanguageExtAdapter(),
    ];

    /// <summary>The name of Rewind's row, against which every ratio is taken.</summary>
    internal const string Rewind = "Rewind";

    /// <summary>A fresh instance of every adapter, in roster order.</summary>
    internal static IReadOnlyList<MarketAdapter> All() => [.. Factories.Select(factory => factory())];

    /// <summary>The roster's names, in order.</summary>
    internal static IReadOnlyList<string> Names { get; } = [.. All().Select(adapter => adapter.Name)];

    /// <summary>A fresh instance of the adapter named <paramref name="name"/>.</summary>
    internal static MarketAdapter Create(string name) =>
        Factories.Select(factory => factory()).FirstOrDefault(adapter => adapter.Name == name)
        ?? throw new ArgumentException($"No adapter is named '{name}'.", nameof(name));

    /// <summary>
    /// What the roster leaves out, and why — said in the report before any number, so an omission is a statement and
    /// not a silence.
    /// </summary>
    internal static IReadOnlyList<(string Library, string Reason)> Unmeasured { get; } =
    [
        ("System.Transactions alone", "Not comparable: a TransactionScope coordinates resource managers and restores no memory. Rewind joins one (contract §11), which is not a T1 workload."),
        ("LanguageExt.Core 5.x", "A beta on the day of the run; the latest stable release, 4.4.9, is measured."),
        ("T2 — isolation between threads", "Not measured by this track. Rewind has no isolation until IDEA-01, so every Rewind cell of T2 would be Unsupported; that is the finding, recorded here instead of as a table."),
    ];
}

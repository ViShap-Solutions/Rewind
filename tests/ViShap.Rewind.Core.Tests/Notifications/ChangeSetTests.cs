using ViShap.Rewind.Tests.Fixtures;

namespace ViShap.Rewind.Tests.Notifications;

public sealed class ChangeSetTests
{
    private static readonly RewindCell<int> s_cell = new(0);
    private static readonly Counter s_counter = new();
    private static int s_value;

    [Fact]
    public void OnCommitted_NestedRegistration_RunsOnceAfterOutermostCommitWithTreeChangesAndNoScope()
    {
        var outer = new RewindCell<int>(0);
        var inner = new RewindCell<int>(0);
        var calls = new List<(ChangeSet Changes, RewindScope? Scope, int Outer)>();

        Rewind.Run(() =>
        {
            outer.Value = 1;
            Rewind.Run(scope =>
            {
                inner.Value = 2;
                scope.OnCommitted(changes => calls.Add((changes, Rewind.Current, outer.Value)));
            });
            Assert.Empty(calls);
            outer.Value = 3;
        });

        var (changes, scope, seen) = Assert.Single(calls);
        Assert.Null(scope);
        Assert.Equal(3, seen);
        Assert.Equal(new object[] { outer, inner }, changes.Changes.Select(change => change.Owner));
    }

    [Fact]
    public void OnCommitted_ScopeRollsBack_IsDiscarded()
    {
        var cell = new RewindCell<int>(0);
        var calls = 0;

        Assert.Throws<InvalidOperationException>(() => Rewind.Run(scope =>
        {
            cell.Value = 1;
            scope.OnCommitted(_ => calls++);
            throw new InvalidOperationException();
        }));
        Rewind.Run(() =>
        {
            Assert.Throws<InvalidOperationException>(() => Rewind.Run(scope =>
            {
                scope.OnCommitted(_ => calls++);
                throw new InvalidOperationException();
            }));
        });

        Assert.Equal(0, calls);
    }

    [Fact]
    public void ChangeSet_CellsAndProperties_ListedOnceWithValuesAtMarkAndAtCommit()
    {
        var cell = new RewindCell<string>("a");
        var document = new Document { Title = "Draft", Pages = 1 };
        ChangeSet? changes = null;

        Rewind.Run(scope =>
        {
            scope.OnCommitted(set => changes = set);
            document.Title = "One";
            cell.Value = "b";
            Rewind.Run(() =>
            {
                document.Title = "Two";
                cell.Value = "c";
            });
            document.Pages = 2;
        });

        Assert.False(changes!.IsEmpty);
        Assert.Collection(
            changes.Changes,
            change => AssertChange(change, document, "Title", "Draft", "Two"),
            change => AssertChange(change, cell, null, "a", "c"),
            change => AssertChange(change, document, "Pages", 1, 2));
    }

    [Fact]
    public void ChangeSet_ValueBroughtBack_IsAbsentAndSetIsEmpty()
    {
        var cell = new RewindCell<int>(1);
        var document = new Document { Title = "Draft" };
        ChangeSet? changes = null;

        Rewind.Run(scope =>
        {
            scope.OnCommitted(set => changes = set);
            cell.Value = 2;
            document.Title = "Other";
            cell.Value = 1;
            document.Title = "Draft";
        });

        Assert.True(changes!.IsEmpty);
        Assert.Empty(changes.Changes);
    }

    [Fact]
    public void ChangeSet_OwnersEqualByValue_AreKeptApart()
    {
        var first = new RewindCell<int>(0);
        var second = new RewindCell<int>(0);
        var a = new EqualRecordHolder();
        var b = new EqualRecordHolder();
        ChangeSet? changes = null;

        Rewind.Run(scope =>
        {
            scope.OnCommitted(set => changes = set);
            first.Value = 1;
            second.Value = 1;
            a.Cell.Value = 1;
            b.Cell.Value = 1;
        });

        Assert.Equal(4, changes!.Changes.Count);
    }

    [Fact]
    public void ChangeSet_CollectionMutated_AppearsOnceAsContentChange()
    {
        var document = new Document();
        ChangeSet? changes = null;

        Rewind.Run(scope =>
        {
            scope.OnCommitted(set => changes = set);
            document.Lines.Add("a");
            document.Lines.Add("b");
            document.Tags["x"] = 1;
            document.Readers.Clear();
            document.Readers.Add("ana");
        });

        Assert.Collection(
            changes!.Changes,
            change => AssertChange(change, document.Lines, null, null, null),
            change => AssertChange(change, document.Tags, null, null, null),
            change => AssertChange(change, document.Readers, null, null, null));
    }

    [Fact]
    public void Commit_NobodyObserving_AllocatesNoMoreThanRollingBack()
    {
        var committed = Allocations.Measure(static () =>
        {
            using var scope = Rewind.Begin();
            s_cell.Value = ++s_value;
            s_counter.Count = s_value;
            scope.Commit();
        });

        var rolledBack = Allocations.Measure(static () =>
        {
            var scope = Rewind.Begin();
            s_cell.Value = ++s_value;
            s_counter.Count = s_value;
            scope.Dispose();
        });

        Assert.True(committed <= rolledBack, $"commit allocated {committed} B, rollback {rolledBack} B");
    }

    private static void AssertChange(Change change, object owner, string? member, object? oldValue, object? newValue)
    {
        Assert.Same(owner, change.Owner);
        Assert.Equal(member, change.Member);
        Assert.Equal(oldValue, change.OldValue);
        Assert.Equal(newValue, change.NewValue);
    }

    private sealed record EqualRecordHolder
    {
        public RewindCell<int> Cell { get; } = new(0);

        public bool Equals(EqualRecordHolder? other) => other is not null;

        public override int GetHashCode() => 1;
    }
}

using ViShap.Rewind.Tests.Fixtures;

namespace ViShap.Rewind.Tests.Conformance;

public sealed class ConformanceTests
{
    private static readonly PropertySlot s_property = new(0) { Name = "a" };

    [Fact]
    public void Commit_KeepsWrite() => Same(new[] { 2 }, slot =>
    {
        Rewind.Run(() => slot.Value = 2);
        return [slot.Value];
    });

    [Fact]
    public void Rollback_RestoresValueAtScopeStart() => Same(new[] { 1, 1 }, slot =>
    {
        Assert.Throws<InvalidOperationException>(() => Rewind.Run(() =>
        {
            slot.Value = 2;
            slot.Value = 3;
            throw new InvalidOperationException();
        }));
        var afterRun = slot.Value;

        using (Rewind.Begin())
        {
            slot.Value = 4;
        }

        return [afterRun, slot.Value];
    });

    [Fact]
    public void NestedRollback_RestoresOuterValue() => Same(new[] { 2, 2 }, slot =>
    {
        var inside = 0;
        Rewind.Run(() =>
        {
            slot.Value = 2;
            Assert.Throws<InvalidOperationException>(() => Rewind.Run(() =>
            {
                slot.Value = 3;
                throw new InvalidOperationException();
            }));
            inside = slot.Value;
        });
        return [inside, slot.Value];
    });

    [Fact]
    public void NestedCommitThenOuterRollback_RestoresStart() => Same(new[] { 1 }, slot =>
    {
        Assert.Throws<InvalidOperationException>(() => Rewind.Run(() =>
        {
            slot.Value = 2;
            Rewind.Run(() => slot.Value = 3);
            slot.Value = 4;
            throw new InvalidOperationException();
        }));
        return [slot.Value];
    });

    [Fact]
    public void WriteInCompensation_IsPlain() => Same(new[] { 9 }, slot =>
    {
        Assert.Throws<InvalidOperationException>(() => Rewind.Run(scope =>
        {
            scope.Effect(() => { }, () => slot.Value = 9);
            throw new InvalidOperationException();
        }));
        return [slot.Value];
    });

    [Fact]
    public void WriteWithNoScope_IsPlain() => Same(new[] { 5 }, slot =>
    {
        slot.Value = 5;
        return [slot.Value];
    });

    [Fact]
    public void WriteFromAnotherThread_IsNotUndone() => Same(new[] { 7 }, slot =>
    {
        using (Rewind.Begin())
        {
            OtherThread.Run(() => slot.Value = 7);
        }

        return [slot.Value];
    });

    [Fact]
    public void JournalEntries_MatchForTheSameWrites() => Same(new[] { 1, 2, 1 }, slot =>
    {
        using var outer = Rewind.Begin();
        slot.Value = 2;
        slot.Value = 3;
        var afterOuterWrites = outer.Journal.Count;

        int afterInnerWrites;
        using (var inner = Rewind.Begin())
        {
            slot.Value = 4;
            slot.Value = 5;
            afterInnerWrites = inner.Journal.Count;
            inner.Commit();
        }

        _ = slot.Value;
        var afterRead = outer.Journal.Count - afterInnerWrites;
        outer.Commit();
        return [afterOuterWrites, afterInnerWrites, afterRead + 1];
    });

    [Fact]
    public void Write_RewindablePropertyWithNoScope_AllocatesNothing()
    {
        var bytes = Allocations.Measure(static () =>
        {
            for (var i = 0; i < 100; i++)
            {
                s_property.Value = i;
            }

            s_property.Name = "b";
            s_property.Name = "a";
        });

        Assert.Equal(0, bytes);
    }

    [Fact]
    public void RewindIgnore_PropertyIsPlainStorage()
    {
        var slot = new PropertySlot(1) { Name = "a" };

        using (Rewind.Begin())
        {
            slot.Untracked = 5;
            slot.Value = 2;
            slot.Name = "b";
        }

        Assert.Equal(5, slot.Untracked);
        Assert.Equal(1, slot.Value);
        Assert.Equal("a", slot.Name);
    }

    private static void Same(int[] expected, Func<ISlot, int[]> scenario)
    {
        var withCell = scenario(new CellSlot(1));

        var withProperty = scenario(new PropertySlot(1));

        Assert.Equal(expected, withCell);
        Assert.Equal(withCell, withProperty);
        Assert.Null(Rewind.Current);
    }
}

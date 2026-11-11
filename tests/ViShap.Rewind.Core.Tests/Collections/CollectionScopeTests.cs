using ViShap.Rewind.Tests.Fixtures;

namespace ViShap.Rewind.Tests.Collections;

public sealed class CollectionScopeTests
{
    private static readonly RewindList<int> s_list = new(Enumerable.Range(0, 64));
    private static readonly RewindDictionary<int, int> s_dictionary = new(Enumerable.Range(0, 64).Select(i => new KeyValuePair<int, int>(i, i)));
    private static readonly RewindSet<int> s_set = new(Enumerable.Range(0, 64));
    private static int s_sum;

    [Fact]
    public void NestedScopes_InnerRollbackUndoesOnlyInnerMutations()
    {
        var list = new RewindList<int>([1]);
        var dictionary = new RewindDictionary<string, int>();
        var set = new RewindSet<int>();

        Rewind.Run(() =>
        {
            list.Add(2);
            dictionary["outer"] = 1;
            set.Add(1);
            Assert.Throws<InvalidOperationException>(() => Rewind.Run(() =>
            {
                list.Add(3);
                list.Clear();
                dictionary["inner"] = 2;
                dictionary.Clear();
                set.Add(2);
                set.Clear();
                throw new InvalidOperationException();
            }));
        });

        Assert.Equal(new[] { 1, 2 }, list);
        Assert.Equal(new[] { "outer" }, dictionary.Keys);
        Assert.Equal(new[] { 1 }, set);
    }

    [Fact]
    public void NestedScopes_OuterRollbackAfterInnerCommitUndoesBoth()
    {
        var list = new RewindList<int>([1]);
        var dictionary = new RewindDictionary<string, int> { ["a"] = 1 };
        var set = new RewindSet<int>([1]);

        Assert.Throws<InvalidOperationException>(() => Rewind.Run(() =>
        {
            list.Add(2);
            Rewind.Run(() =>
            {
                list.Clear();
                dictionary.Remove("a");
                set.UnionWith([2, 3]);
            });
            list.Add(3);
            throw new InvalidOperationException();
        }));

        Assert.Equal(new[] { 1 }, list);
        Assert.Equal(1, dictionary["a"]);
        Assert.Equal(new[] { 1 }, set);
    }

    [Fact]
    public void Mutation_WithNoScope_AllocatesNothing()
    {
        var bytes = Allocations.Measure(static () =>
        {
            s_list[0] = 1;
            s_list.RemoveAt(10);
            s_list.Insert(10, 10);
            s_list.Add(64);
            s_list.RemoveAt(64);
            s_dictionary[0] = 1;
            s_dictionary.Remove(10);
            s_dictionary.Add(10, 10);
            s_set.Remove(10);
            s_set.Add(10);
        });

        Assert.Equal(0, bytes);
    }

    [Fact]
    public void Enumerate_WithNoScope_AllocatesNothing()
    {
        var bytes = Allocations.Measure(static () =>
        {
            var sum = 0;
            foreach (var item in s_list)
            {
                sum += item;
            }

            foreach (var pair in s_dictionary)
            {
                sum += pair.Value;
            }

            foreach (var item in s_set)
            {
                sum += item;
            }

            s_sum = sum;
        });

        Assert.Equal(0, bytes);
    }

    [Fact]
    public void OneMutation_OfLargeCollection_RecordsOneEntry()
    {
        var list = new RewindList<int>(Enumerable.Range(0, 100_000));
        var dictionary = new RewindDictionary<int, int>(Enumerable.Range(0, 100_000).Select(i => new KeyValuePair<int, int>(i, i)));
        var set = new RewindSet<int>(Enumerable.Range(0, 100_000));
        using var scope = Rewind.Begin();

        list[50_000] = -1;
        dictionary[50_000] = -1;
        set.Remove(50_000);

        Assert.Equal(3, scope.Journal.Count);
        scope.Dispose();
        var restored = set.Contains(50_000);
        Assert.Equal(50_000, list[50_000]);
        Assert.Equal(50_000, dictionary[50_000]);
        Assert.True(restored);
    }

    [Fact]
    public void Clear_InScope_RecordsOneEntryAndAllocatesTheSameForAnySize()
    {
        var small = ClearInScope(10);

        var large = ClearInScope(100_000);

        Assert.Equal(small, large);
    }

    [Fact]
    public void Write_FromAnotherThreadWithNoScope_IsPlainAndNotUndone()
    {
        var list = new RewindList<int>([1]);
        var dictionary = new RewindDictionary<string, int>();
        var set = new RewindSet<int>();

        using (Rewind.Begin())
        {
            OtherThread.Run(() =>
            {
                list.Add(2);
                dictionary["a"] = 1;
                return set.Add(1);
            });
        }

        Assert.Equal(new[] { 1, 2 }, list);
        Assert.Equal(1, dictionary["a"]);
        Assert.Equal(new[] { 1 }, set);
    }

    [Fact]
    public void Undo_RunsWithNoCurrentScope_SoCompensationMutationsArePlain()
    {
        var list = new RewindList<int>();

        Assert.Throws<InvalidOperationException>(() => Rewind.Run(scope =>
        {
            scope.Effect(() => { }, () => list.Add(99));
            throw new InvalidOperationException();
        }));

        Assert.Equal(new[] { 99 }, list);
    }

    private static (int Entries, long Bytes) ClearInScope(int size)
    {
        var list = new RewindList<int>(Enumerable.Range(0, size));
        var dictionary = new RewindDictionary<int, int>(Enumerable.Range(0, size).Select(i => new KeyValuePair<int, int>(i, i)));
        var set = new RewindSet<int>(Enumerable.Range(0, size));
        using var scope = Rewind.Begin();

        var before = GC.GetAllocatedBytesForCurrentThread();
        list.Clear();
        dictionary.Clear();
        set.Clear();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        var entries = scope.Journal.Count;
        scope.Dispose();
        Assert.Equal(size, list.Count);
        Assert.Equal(size, dictionary.Count);
        Assert.Equal(size, set.Count);
        return (entries, bytes);
    }
}

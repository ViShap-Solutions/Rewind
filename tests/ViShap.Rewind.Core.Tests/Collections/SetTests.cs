namespace ViShap.Rewind.Tests.Collections;

public sealed class SetTests
{
    [Fact]
    public void Rollback_AddAndRemove_RestoresMembership()
    {
        var set = new RewindSet<int>([1, 2, 3]);

        using (Rewind.Begin())
        {
            Assert.True(set.Add(4));
            Assert.True(set.Remove(1));
            ((ICollection<int>)set).Add(5);
        }

        Assert.Equal(new[] { 1, 2, 3 }, Sorted(set));
        Assert.Null(Rewind.Current);
    }

    [Fact]
    public void Rollback_SetOperations_RestoresMembership()
    {
        var set = new RewindSet<int>([1, 2, 3, 4]);

        using (Rewind.Begin())
        {
            set.UnionWith([4, 5, 6]);
            set.ExceptWith([1, 9]);
            set.IntersectWith([2, 3, 5, 6, 7]);
            set.SymmetricExceptWith([3, 8]);
        }

        Assert.Equal(new[] { 1, 2, 3, 4 }, Sorted(set));
    }

    [Fact]
    public void SetOperations_ProduceTheirResultAndRecordOneEntryEach()
    {
        var set = new RewindSet<int>([1, 2, 3, 4]);
        using var scope = Rewind.Begin();

        set.UnionWith([4, 5, 6]);
        set.ExceptWith([1, 9]);
        set.IntersectWith([2, 3, 5, 6, 7]);
        set.SymmetricExceptWith([3, 8, 8]);

        Assert.Equal(new[] { 2, 5, 6, 8 }, Sorted(set));
        Assert.Equal(4, scope.Journal.Count);
        scope.Commit();
    }

    [Fact]
    public void SetOperations_WithItself_BehaveAsHashSet()
    {
        var set = new RewindSet<int>([1, 2]);

        using (Rewind.Begin())
        {
            set.IntersectWith(set);
            Assert.Equal(new[] { 1, 2 }, Sorted(set));
            set.ExceptWith(set);
            Assert.Empty(set);
        }

        Assert.Equal(new[] { 1, 2 }, Sorted(set));
    }

    [Fact]
    public void Clear_InScopeRolledBack_RestoresContent()
    {
        var set = new RewindSet<int>([1, 2]);

        using (Rewind.Begin())
        {
            set.Clear();
            set.Add(3);

            Assert.Equal(new[] { 3 }, Sorted(set));
        }

        Assert.Equal(new[] { 1, 2 }, Sorted(set));
    }

    [Fact]
    public void Clear_InScopeCommitted_KeepsEmpty()
    {
        var set = new RewindSet<int>([1, 2]);

        Rewind.Run(set.Clear);

        Assert.Empty(set);
    }

    [Fact]
    public void Mutation_ThatChangesNothing_RecordsNothing()
    {
        var set = new RewindSet<int>([1, 2]);
        using var scope = Rewind.Begin();

        Assert.False(set.Add(1));
        Assert.False(set.Remove(9));
        set.UnionWith([1, 2]);
        set.ExceptWith([7]);
        set.IntersectWith([1, 2, 3]);
        set.SymmetricExceptWith([]);
        new RewindSet<int>().Clear();

        Assert.Equal(0, scope.Journal.Count);
        scope.Commit();
    }

    [Fact]
    public void Comparer_IsUsedAndSurvivesClearInScope()
    {
        var set = new RewindSet<string>(["Key"], StringComparer.OrdinalIgnoreCase);

        using (Rewind.Begin())
        {
            set.Clear();
            set.Add("KEY");

            Assert.False(set.Add("key"));
            Assert.Same(StringComparer.OrdinalIgnoreCase, set.Comparer);
        }

        var contains = set.Contains("KEY");
        Assert.True(contains);
    }

    [Fact]
    public void Enumerate_SetMutated_Throws()
    {
        var set = new RewindSet<int>([1, 2]);

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var item in set)
            {
                set.Add(item + 10);
            }
        });
    }

    [Fact]
    public void Enumerate_Rollback_Throws()
    {
        var set = new RewindSet<int>([1, 2]);
        var scope = Rewind.Begin();
        set.Add(3);
        var enumerator = set.GetEnumerator();

        scope.Dispose();

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void Queries_ReflectContent()
    {
        var set = new RewindSet<int>([1, 2, 3]);
        var copy = new int[3];

        set.CopyTo(copy, 0);

        Assert.Equal(new[] { 1, 2, 3 }, copy.Order());
        Assert.True(set.IsSubsetOf([1, 2, 3, 4]));
        Assert.True(set.IsProperSubsetOf([1, 2, 3, 4]));
        Assert.True(set.IsSupersetOf([1, 2]));
        Assert.True(set.IsProperSupersetOf([1, 2]));
        Assert.True(set.Overlaps([3, 9]));
        Assert.True(set.SetEquals([3, 2, 1]));
        Assert.Equal(3, set.Count);
    }

    private static int[] Sorted(IEnumerable<int> items) => items.Order().ToArray();
}

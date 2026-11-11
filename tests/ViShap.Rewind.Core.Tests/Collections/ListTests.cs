namespace ViShap.Rewind.Tests.Collections;

public sealed class ListTests
{
    [Fact]
    public void Rollback_EveryMutation_RestoresContentAndOrder()
    {
        var list = new RewindList<string>(["a", "b", "c"]);

        using (Rewind.Begin())
        {
            list.Add("d");
            list.AddRange(["e", "f"]);
            list.Insert(0, "z");
            list[2] = "B";
            list.RemoveAt(3);
            list.Remove("a");
            list.Insert(list.Count, "end");
        }

        Assert.Equal(new[] { "a", "b", "c" }, list);
        Assert.Null(Rewind.Current);
    }

    [Fact]
    public void Commit_EveryMutation_KeepsContent()
    {
        var list = new RewindList<int>([1, 2, 3]);

        Rewind.Run(() =>
        {
            list.Add(4);
            list.RemoveAt(0);
            list[0] = 20;
        });

        Assert.Equal(new[] { 20, 3, 4 }, list);
    }

    [Fact]
    public void Rollback_DuplicateElements_RestoresPositions()
    {
        var list = new RewindList<int>([1, 2, 1, 2]);

        using (Rewind.Begin())
        {
            list.Remove(2);
            list.Remove(1);
            list.Insert(1, 1);
        }

        Assert.Equal(new[] { 1, 2, 1, 2 }, list);
    }

    [Fact]
    public void Clear_InScopeRolledBack_RestoresContent()
    {
        var list = new RewindList<int>([1, 2, 3]);

        using (Rewind.Begin())
        {
            list.Add(4);
            list.Clear();
            list.Add(9);

            Assert.Equal(new[] { 9 }, list);
        }

        Assert.Equal(new[] { 1, 2, 3 }, list);
    }

    [Fact]
    public void Clear_InScopeCommitted_KeepsEmpty()
    {
        var list = new RewindList<int>([1, 2, 3]);

        Rewind.Run(list.Clear);

        Assert.Empty(list);
    }

    [Fact]
    public void Clear_WithNoScope_Empties()
    {
        var list = new RewindList<int>([1, 2, 3]);

        list.Clear();

        Assert.Empty(list);
    }

    [Fact]
    public void Mutation_ThatChangesNothingOrThrows_RecordsNothing()
    {
        var list = new RewindList<int>([1]);
        var empty = new RewindList<int>();
        using var scope = Rewind.Begin();

        Assert.False(list.Remove(42));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.Insert(5, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.RemoveAt(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => list[3] = 1);
        list.AddRange([]);
        empty.Clear();

        Assert.Equal(0, scope.Journal.Count);
        Assert.Equal(new[] { 1 }, list);
        scope.Commit();
    }

    [Fact]
    public void Rollback_ReferenceElements_RestoresMembershipNotContent()
    {
        var item = new List<int> { 1 };
        var list = new RewindList<List<int>>([item]);

        using (Rewind.Begin())
        {
            item.Add(2);
            list.RemoveAt(0);
        }

        Assert.Same(item, Assert.Single(list));
        Assert.Equal(new[] { 1, 2 }, item);
    }

    [Fact]
    public void Enumerate_ListMutated_Throws()
    {
        var list = new RewindList<int>([1, 2, 3]);

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var item in list)
            {
                list.Add(item);
            }
        });
    }

    [Fact]
    public void Enumerate_ClearInScope_Throws()
    {
        var list = new RewindList<int>([1, 2, 3]);
        using var scope = Rewind.Begin();
        var enumerator = list.GetEnumerator();
        enumerator.MoveNext();

        list.Clear();

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
        scope.Commit();
    }

    [Fact]
    public void Enumerate_Rollback_Throws()
    {
        var list = new RewindList<int>([1, 2, 3]);
        var scope = Rewind.Begin();
        list.Add(4);
        var enumerator = list.GetEnumerator();

        scope.Dispose();

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void Enumerate_ThroughInterface_ReturnsElementsInOrder()
    {
        IEnumerable<int> list = new RewindList<int>([3, 1, 2]);

        Assert.Equal(new[] { 3, 1, 2 }, list.ToArray());
    }

    [Fact]
    public void Queries_ReflectContent()
    {
        var list = new RewindList<string>(["a", "b"]);
        var copy = new string[3];

        list.CopyTo(copy, 1);

        Assert.Equal(2, list.Count);
        Assert.Equal("b", list[1]);
        var contains = list.Contains("a");
        Assert.True(contains);
        Assert.Equal(1, list.IndexOf("b"));
        Assert.Equal(new[] { null, "a", "b" }, copy);
        Assert.False(((ICollection<string>)list).IsReadOnly);
    }
}

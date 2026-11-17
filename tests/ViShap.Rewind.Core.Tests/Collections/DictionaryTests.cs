namespace ViShap.Rewind.Tests.Collections;

public sealed class DictionaryTests
{
    [Fact]
    public void Rollback_EveryMutation_RestoresKeysAndValues()
    {
        var dictionary = new RewindDictionary<string, int>(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2, ["c"] = 3 });

        using (Rewind.Begin())
        {
            dictionary.Add("d", 4);
            Assert.True(dictionary.TryAdd("e", 5));
            dictionary["a"] = 10;
            dictionary["f"] = 6;
            Assert.True(dictionary.Remove("b"));
            Assert.True(dictionary.Remove("c", out var removed));
            Assert.Equal(3, removed);
        }

        Assert.Equal(Pairs(("a", 1), ("b", 2), ("c", 3)), Sorted(dictionary));
        Assert.Null(Rewind.Current);
    }

    [Fact]
    public void Rollback_SameKeyChangedSeveralTimes_RestoresStartValue()
    {
        var dictionary = new RewindDictionary<string, int> { ["a"] = 1 };

        using (Rewind.Begin())
        {
            dictionary["a"] = 2;
            dictionary.Remove("a");
            dictionary["a"] = 3;
            dictionary.Add("b", 4);
            dictionary.Remove("b");
        }

        Assert.Equal(Pairs(("a", 1)), Sorted(dictionary));
    }

    [Fact]
    public void Commit_EveryMutation_KeepsContent()
    {
        var dictionary = new RewindDictionary<string, int> { ["a"] = 1 };

        Rewind.Run(() =>
        {
            dictionary["a"] = 2;
            dictionary.Add("b", 3);
        });

        Assert.Equal(Pairs(("a", 2), ("b", 3)), Sorted(dictionary));
    }

    [Fact]
    public void Clear_InScopeRolledBack_RestoresContent()
    {
        var dictionary = new RewindDictionary<string, int> { ["a"] = 1, ["b"] = 2 };

        using (Rewind.Begin())
        {
            dictionary.Clear();
            dictionary["c"] = 3;

            Assert.Equal(Pairs(("c", 3)), Sorted(dictionary));
        }

        Assert.Equal(Pairs(("a", 1), ("b", 2)), Sorted(dictionary));
    }

    [Fact]
    public void Clear_InScopeCommitted_KeepsEmpty()
    {
        var dictionary = new RewindDictionary<string, int> { ["a"] = 1 };

        Rewind.Run(dictionary.Clear);

        Assert.Empty(dictionary);
    }

    [Fact]
    public void Mutation_ThatChangesNothingOrThrows_RecordsNothing()
    {
        var dictionary = new RewindDictionary<string, int> { ["a"] = 1 };
        using var scope = Rewind.Begin();

        Assert.Throws<ArgumentException>(() => dictionary.Add("a", 2));
        Assert.False(dictionary.TryAdd("a", 2));
        Assert.False(dictionary.Remove("missing"));
        Assert.False(((ICollection<KeyValuePair<string, int>>)dictionary).Remove(new KeyValuePair<string, int>("a", 9)));
        Assert.Throws<ArgumentNullException>(() => dictionary[null!] = 1);
        new RewindDictionary<string, int>().Clear();

        Assert.Equal(0, scope.Journal.Count);
        Assert.Equal(1, dictionary["a"]);
        scope.Commit();
    }

    [Fact]
    public void Comparer_IsUsedAndSurvivesClearInScope()
    {
        var dictionary = new RewindDictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Key"] = 1 };

        using (Rewind.Begin())
        {
            dictionary.Clear();
            dictionary["KEY"] = 2;

            Assert.True(dictionary.ContainsKey("key"));
            Assert.Same(StringComparer.OrdinalIgnoreCase, dictionary.Comparer);
        }

        Assert.Equal(1, dictionary["KEY"]);
    }

    [Fact]
    public void KeysAndValues_FollowContentAcrossClearAndRollback()
    {
        var dictionary = new RewindDictionary<string, int> { ["a"] = 1 };
        var keys = dictionary.Keys;
        var values = dictionary.Values;

        using (Rewind.Begin())
        {
            dictionary.Clear();
            dictionary["b"] = 2;

            Assert.Equal(new[] { "b" }, keys);
            Assert.Equal(new[] { 2 }, values);
        }

        Assert.Equal(new[] { "a" }, keys);
        Assert.Equal(new[] { 1 }, values);
        Assert.True(keys.Contains("a"));
        Assert.True(values.Contains(1));
        Assert.True(keys.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => keys.Add("x"));
    }

    [Fact]
    public void Enumerate_DictionaryMutated_Throws()
    {
        var dictionary = new RewindDictionary<string, int> { ["a"] = 1, ["b"] = 2 };

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var pair in dictionary)
            {
                dictionary[pair.Key + "x"] = pair.Value;
            }
        });
    }

    [Fact]
    public void Enumerate_ClearInScope_Throws()
    {
        var dictionary = new RewindDictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        using var scope = Rewind.Begin();
        var enumerator = dictionary.GetEnumerator();
        enumerator.MoveNext();

        dictionary.Clear();

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
        scope.Commit();
    }

    [Fact]
    public void Enumerate_KeysWhileRollback_Throws()
    {
        var dictionary = new RewindDictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var scope = Rewind.Begin();
        dictionary["c"] = 3;
        using var keys = dictionary.Keys.GetEnumerator();
        keys.MoveNext();

        scope.Dispose();

        Assert.Throws<InvalidOperationException>(() => keys.MoveNext());
    }

    [Fact]
    public void PairOperations_ThroughCollectionInterface_AreRecorded()
    {
        var dictionary = new RewindDictionary<string, int> { ["a"] = 1 };
        ICollection<KeyValuePair<string, int>> pairs = dictionary;

        using (Rewind.Begin())
        {
            pairs.Add(new KeyValuePair<string, int>("b", 2));
            Assert.True(pairs.Contains(new KeyValuePair<string, int>("a", 1)));
            Assert.True(pairs.Remove(new KeyValuePair<string, int>("a", 1)));
        }

        Assert.Equal(Pairs(("a", 1)), Sorted(dictionary));
    }

    [Fact]
    public void Queries_ReflectContent()
    {
        var dictionary = new RewindDictionary<string, int> { ["a"] = 1 };
        IReadOnlyDictionary<string, int> readOnly = dictionary;

        Assert.True(dictionary.TryGetValue("a", out var value));
        Assert.Equal(1, value);
        Assert.False(dictionary.TryGetValue("b", out _));
        Assert.Throws<KeyNotFoundException>(() => dictionary["b"]);
        Assert.Equal(new[] { "a" }, readOnly.Keys);
        Assert.Equal(new[] { 1 }, readOnly.Values);
        Assert.Single(dictionary);
    }

    private static KeyValuePair<string, int>[] Pairs(params (string Key, int Value)[] pairs) =>
        pairs.Select(pair => new KeyValuePair<string, int>(pair.Key, pair.Value)).ToArray();

    private static KeyValuePair<string, int>[] Sorted(IEnumerable<KeyValuePair<string, int>> pairs) =>
        pairs.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
}

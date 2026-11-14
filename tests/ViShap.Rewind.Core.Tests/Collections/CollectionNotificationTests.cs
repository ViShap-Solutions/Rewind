using System.Collections.Specialized;
using System.ComponentModel;
using System.Transactions;

namespace ViShap.Rewind.Tests.Collections;

public sealed class CollectionNotificationTests
{
    [Fact]
    public void List_WithNoScope_RaisesEachChangeAtOnceWithItsPosition()
    {
        var list = new RewindList<string>(["a", "b"]);
        var log = new CollectionLog(list);

        list.Add("c");
        list.Insert(0, "z");
        list[1] = "A";
        list.RemoveAt(3);
        list.Remove("b");

        Assert.Equal(
        [
            "Add c @2", "Count", "Item[]",
            "Add z @0", "Count", "Item[]",
            "Replace A<-a @1", "Item[]",
            "Remove c @3", "Count", "Item[]",
            "Remove b @2", "Count", "Item[]",
        ], log.Events);
        Assert.Equal(["z", "A"], list);
    }

    [Fact]
    public void List_WithNoScope_AddRangeOfOneIsAnAdd_OfSeveralIsAReset_ClearIsAReset()
    {
        var list = new RewindList<int>();
        var log = new CollectionLog(list);

        list.AddRange([1]);
        list.AddRange([2, 3]);
        list.AddRange([]);
        list.Clear();
        list.Clear();

        Assert.Equal(["Add 1 @0", "Count", "Item[]", "Reset", "Count", "Item[]", "Reset", "Count", "Item[]"], log.Events);
    }

    [Fact]
    public void List_InScope_SilentWhileOpen_OneResetAfterTheCommit_WithNoScopeAndTheCommittedContent()
    {
        var list = new RewindList<int>([1]);
        var log = new CollectionLog(list);

        Rewind.Run(() =>
        {
            list.Add(2);
            list.Insert(0, 0);
            list[1] = 10;
            Rewind.Run(() => list.RemoveAt(2));
            Assert.Empty(log.Events);
        });

        Assert.Equal(["Reset", "Count", "Item[]"], log.Events);
        Assert.Equal([0, 10], log.ContentAtFirstEvent);
        Assert.All(log.Scopes, Assert.Null);
    }

    [Fact]
    public void List_RolledBack_OneResetAfterTheRestore()
    {
        var list = new RewindList<int>([1, 2]);
        var log = new CollectionLog(list);

        Assert.Throws<InvalidOperationException>(() => Rewind.Run(() =>
        {
            list.Clear();
            list.Add(9);
            throw new InvalidOperationException();
        }));

        Assert.Equal(["Reset", "Count", "Item[]"], log.Events);
        Assert.Equal([1, 2], log.ContentAtFirstEvent);
        Assert.Equal([1, 2], list);
    }

    [Fact]
    public void List_NestedRollbackInsideCommittedScope_OneResetWhenTheOutermostEnds()
    {
        var list = new RewindList<int>([1]);
        var log = new CollectionLog(list);

        Rewind.Run(() =>
        {
            using (Rewind.Begin())
            {
                list.Add(2);
            }

            Assert.Empty(log.Events);
        });

        Assert.Equal(["Reset", "Count", "Item[]"], log.Events);
        Assert.Equal([1], list);
    }

    [Fact]
    public void DictionaryAndSet_RaiseNoCollectionChanged_SoUiListsDoNotRequireAnIList()
    {
        Assert.False(typeof(INotifyCollectionChanged).IsAssignableFrom(typeof(RewindDictionary<string, int>)));
        Assert.False(typeof(INotifyCollectionChanged).IsAssignableFrom(typeof(RewindSet<int>)));
        Assert.True(typeof(System.Collections.IList).IsAssignableFrom(typeof(RewindList<int>)));
    }

    [Fact]
    public void Dictionary_WithNoScope_EachChangeRaisesCountIndexerKeysValues()
    {
        var dictionary = new RewindDictionary<string, int>();
        var log = new CollectionLog(dictionary);

        dictionary.Add("a", 1);
        dictionary["a"] = 2;
        dictionary.TryAdd("a", 3);
        dictionary.Remove("a");
        dictionary.Remove("a");

        Assert.Equal(
        [
            "Count", "Item[]", "Keys", "Values",
            "Item[]", "Keys", "Values",
            "Count", "Item[]", "Keys", "Values",
        ], log.Events);
    }

    [Fact]
    public void Dictionary_InScope_RaisedOnceAfterCommitAndOnceAfterRollback()
    {
        var dictionary = new RewindDictionary<string, int>();
        var log = new CollectionLog(dictionary);

        Rewind.Run(() =>
        {
            dictionary["a"] = 1;
            dictionary["b"] = 2;
        });
        using (Rewind.Begin())
        {
            dictionary.Clear();
        }

        Assert.Equal(["Count", "Item[]", "Keys", "Values", "Count", "Item[]", "Keys", "Values"], log.Events);
        Assert.Equal(2, dictionary.Count);
    }

    [Fact]
    public void Set_WithNoScope_ChangesRaiseCount_NoChangeRaisesNothing()
    {
        var set = new RewindSet<int>([1]);
        var log = new CollectionLog(set);

        set.Add(2);
        set.Add(2);
        set.UnionWith([2, 3]);
        set.ExceptWith([9]);
        set.Remove(1);
        set.Clear();

        Assert.Equal(["Count", "Count", "Count", "Count"], log.Events);
    }

    [Fact]
    public void Set_InScope_RaisedOnceAfterCommit()
    {
        var set = new RewindSet<int>();
        var log = new CollectionLog(set);

        Rewind.Run(() =>
        {
            set.Add(1);
            set.SymmetricExceptWith([1, 2, 3]);
        });

        Assert.Equal(["Count"], log.Events);
        Assert.Equal([2, 3], set.Order());
    }

    [Fact]
    public void List_NonGenericIList_ChangesThroughItAreRecordedAndNotified()
    {
        var list = new RewindList<string>(["a"]);
        var untyped = (System.Collections.IList)list;
        var log = new CollectionLog(list);

        using (Rewind.Begin())
        {
            untyped.Add("b");
            untyped.Insert(0, "z");
            untyped[1] = "A";
            untyped.Remove("b");
        }

        Assert.Equal(["a"], list);
        Assert.Equal(["Reset", "Count", "Item[]"], log.Events);
        Assert.Equal(0, untyped.IndexOf("a"));
        Assert.Equal(-1, untyped.IndexOf(42));
        Assert.False(untyped.Contains(42));
        Assert.Throws<ArgumentException>(() => untyped.Add(42));
        Assert.Equal(["a"], list);
    }

    [Fact]
    public void History_UndoAndRedo_EachRaiseOneReset()
    {
        var list = new RewindList<string>();
        var history = new RewindHistory();
        history.Run(() => list.Add("milk"));
        var log = new CollectionLog(list);

        history.Undo();
        history.Redo();

        Assert.Equal(["Reset", "Count", "Item[]", "Reset", "Count", "Item[]"], log.Events);
        Assert.Equal(["milk"], list);
    }

    [Fact]
    public void AmbientTransaction_RolledBack_OneResetAfterTheTransaction()
    {
        var list = new RewindList<int>([1]);
        var log = new CollectionLog(list);

        using (new TransactionScope())
        {
            Rewind.Run(() => list.Add(2));
            Assert.Empty(log.Events);
        }

        Assert.Equal(["Reset", "Count", "Item[]"], log.Events);
        Assert.Equal([1], log.ContentAtFirstEvent);
        Assert.Equal([1], list);
    }

    [Fact]
    public void SubscribedOnlyToPropertyChanged_CountAndIndexerStillRaised()
    {
        var list = new RewindList<int>();
        var names = new List<string>();
        ((INotifyPropertyChanged)list).PropertyChanged += (_, e) => names.Add(e.PropertyName!);

        list.Add(1);
        Rewind.Run(() => list.Add(2));

        Assert.Equal(["Count", "Item[]", "Count", "Item[]"], names);
    }

    [Fact]
    public void HandlerThrowsAfterCommit_StateCommitted_OtherCollectionsNotified_EffectExceptionThrown()
    {
        var first = new RewindList<int>();
        var second = new RewindList<int>();
        first.CollectionChanged += (_, _) => throw new FormatException("handler");
        var log = new CollectionLog(second);

        var thrown = Assert.Throws<RewindEffectException>(() => Rewind.Run(() =>
        {
            first.Add(1);
            second.Add(2);
        }));

        Assert.IsType<FormatException>(Assert.Single(thrown.Failures));
        Assert.Equal([1], first);
        Assert.Equal(["Reset", "Count", "Item[]"], log.Events);
    }

    [Fact]
    public void Unsubscribed_RaisesNothing_AndARollbackStillRestores()
    {
        var list = new RewindList<int>([1]);
        var events = 0;
        NotifyCollectionChangedEventHandler handler = (_, _) => events++;
        list.CollectionChanged += handler;
        list.CollectionChanged -= handler;

        list.Add(2);
        using (Rewind.Begin())
        {
            list.Add(3);
        }

        Assert.Equal(0, events);
        Assert.Equal([1, 2], list);
    }

    [Fact]
    public void UnsubscribedWhileTheScopeIsOpen_NothingRaisedWhenItEnds()
    {
        var list = new RewindList<int>();
        var events = 0;
        NotifyCollectionChangedEventHandler handler = (_, _) => events++;
        list.CollectionChanged += handler;

        Rewind.Run(() =>
        {
            list.Add(1);
            list.CollectionChanged -= handler;
        });

        Assert.Equal(0, events);
        Assert.Equal([1], list);
    }

    /// <summary>Records the collection and property notifications of a Rewind collection, as text.</summary>
    private sealed class CollectionLog
    {
        public CollectionLog(object collection)
        {
            if (collection is INotifyCollectionChanged observable)
            {
                observable.CollectionChanged += (_, e) =>
                {
                    Events.Add(Describe(e));
                    Scopes.Add(Rewind.Current);
                    ContentAtFirstEvent ??= collection is System.Collections.IEnumerable items ? [.. items.Cast<object>().Select(item => item is int number ? number : 0)] : null;
                };
            }

            ((INotifyPropertyChanged)collection).PropertyChanged += (_, e) => Events.Add(e.PropertyName!);
        }

        public List<string> Events { get; } = [];

        public List<RewindScope?> Scopes { get; } = [];

        public List<int>? ContentAtFirstEvent { get; private set; }

        private static string Describe(NotifyCollectionChangedEventArgs e) => e.Action switch
        {
            NotifyCollectionChangedAction.Add => $"Add {e.NewItems![0]} @{e.NewStartingIndex}",
            NotifyCollectionChangedAction.Remove => $"Remove {e.OldItems![0]} @{e.OldStartingIndex}",
            NotifyCollectionChangedAction.Replace => $"Replace {e.NewItems![0]}<-{e.OldItems![0]} @{e.NewStartingIndex}",
            _ => e.Action.ToString(),
        };
    }
}

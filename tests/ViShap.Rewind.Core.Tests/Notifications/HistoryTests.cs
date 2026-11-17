using ViShap.Rewind.Tests.Fixtures;

namespace ViShap.Rewind.Tests.Notifications;

public sealed class HistoryTests
{
    [Fact]
    public void UndoRedo_CellsAndProperties_RevertAndReapply()
    {
        var cell = new RewindCell<int>(1);
        var document = new Document { Title = "Draft", Pages = 1 };
        var history = new RewindHistory();

        history.Run(() =>
        {
            cell.Value = 2;
            document.Title = "One";
            document.Title = "Two";
            Rewind.Run(() => document.Pages = 5);
        });
        history.Undo();

        Assert.Equal((1, "Draft", 1), (cell.Value, document.Title, document.Pages));

        history.Redo();

        Assert.Equal((2, "Two", 5), (cell.Value, document.Title, document.Pages));

        history.Undo();

        Assert.Equal((1, "Draft", 1), (cell.Value, document.Title, document.Pages));
    }

    [Fact]
    public void UndoRedo_ListMutations_RevertAndReapply()
    {
        var list = new RewindList<string>(["a", "b", "c"]);
        var history = new RewindHistory();

        history.Run(() =>
        {
            list.Add("d");
            list.Insert(0, "z");
            list.RemoveAt(2);
            list[1] = "A";
            list.AddRange(["e", "f"]);
        });
        var after = list.ToArray();
        history.Run(() =>
        {
            list.Clear();
            list.Add("only");
        });

        history.Undo();
        Assert.Equal(after, list);
        history.Undo();
        Assert.Equal(new[] { "a", "b", "c" }, list);
        history.Redo();
        Assert.Equal(after, list);
        history.Redo();
        Assert.Equal(new[] { "only" }, list);
        history.Undo();
        history.Undo();
        Assert.Equal(new[] { "a", "b", "c" }, list);
    }

    [Fact]
    public void UndoRedo_DictionaryAndSetMutations_RevertAndReapply()
    {
        var dictionary = new RewindDictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var set = new RewindSet<int>([1, 2, 3]);
        var history = new RewindHistory();

        history.Run(() =>
        {
            dictionary["a"] = 10;
            dictionary.Add("c", 3);
            dictionary.Remove("b");
            set.Add(4);
            set.Remove(1);
            set.UnionWith([5, 6]);
            set.SymmetricExceptWith([2, 7]);
        });
        history.Run(() =>
        {
            dictionary.Clear();
            set.Clear();
        });

        history.Undo();
        Assert.Equal(new[] { ("a", 10), ("c", 3) }, dictionary.OrderBy(p => p.Key).Select(p => (p.Key, p.Value)));
        Assert.Equal(new[] { 3, 4, 5, 6, 7 }, set.Order());
        history.Undo();
        Assert.Equal(new[] { ("a", 1), ("b", 2) }, dictionary.OrderBy(p => p.Key).Select(p => (p.Key, p.Value)));
        Assert.Equal(new[] { 1, 2, 3 }, set.Order());
        history.Redo();
        Assert.Equal(new[] { ("a", 10), ("c", 3) }, dictionary.OrderBy(p => p.Key).Select(p => (p.Key, p.Value)));
        Assert.Equal(new[] { 3, 4, 5, 6, 7 }, set.Order());
        history.Redo();
        Assert.Empty(dictionary);
        Assert.Empty(set);
    }

    [Fact]
    public void History_TracksWhatCanBeUndoneAndRedone()
    {
        var cell = new RewindCell<int>(0);
        var history = new RewindHistory();

        Assert.False(history.CanUndo);
        Assert.Throws<InvalidOperationException>(history.Undo);
        Assert.Throws<InvalidOperationException>(history.Redo);

        history.Run(() => cell.Value = 1);
        history.Run(() => cell.Value = 1);
        history.Run(scope => cell.Value = 2);
        history.Undo();

        Assert.True(history.CanUndo);
        Assert.True(history.CanRedo);

        history.Undo();
        Assert.False(history.CanUndo);
        Assert.Equal(0, cell.Value);

        history.Redo();
        history.Run(() => cell.Value = 3);

        Assert.False(history.CanRedo);

        history.Clear();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal(3, cell.Value);
    }

    [Fact]
    public void UndoRedo_AreScopes_NotificationsFollowTheirCommit()
    {
        var document = new Document { Title = "Draft" };
        var history = new RewindHistory();
        history.Run(() => document.Title = "Final");
        var log = new NotificationLog(document, _ => document.Title);

        history.Undo();
        history.Redo();

        Assert.Equal(new[] { "Title", "Title" }, log.Names);
        Assert.Equal(new object?[] { "Draft", "Final" }, log.Values);
        Assert.All(log.Scopes, Assert.Null);
    }

    [Fact]
    public void Undo_Fails_RollsBackAndKeepsStep()
    {
        var cell = new RewindCell<int>(0);
        var list = new RewindList<int>();
        var history = new RewindHistory();
        history.Run(() =>
        {
            cell.Value = 1;
            list.AddRange([1, 2, 3]);
        });
        list.Clear();

        Assert.Throws<ArgumentException>(history.Undo);

        Assert.Equal(1, cell.Value);
        Assert.Empty(list);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Null(Rewind.Current);
    }

    [Fact]
    public void UndoRedo_WithScopeOpen_ThrowsScope()
    {
        var cell = new RewindCell<int>(0);
        var history = new RewindHistory();
        history.Run(() => cell.Value = 1);

        using (var scope = Rewind.Begin())
        {
            Assert.Throws<RewindScopeException>(history.Undo);
            scope.Commit();
        }

        Assert.Equal(1, cell.Value);
        Assert.True(history.CanUndo);
    }

    [Fact]
    public void Run_InsideOuterScope_RecordsOnlyWhenOuterCommits()
    {
        var cell = new RewindCell<int>(0);
        var history = new RewindHistory();

        Assert.Throws<InvalidOperationException>(() => Rewind.Run(() =>
        {
            history.Run(() => cell.Value = 1);
            throw new InvalidOperationException();
        }));

        Assert.False(history.CanUndo);

        Rewind.Run(() =>
        {
            history.Run(() => cell.Value = 2);
            Assert.False(history.CanUndo);
        });

        Assert.True(history.CanUndo);
        history.Undo();
        Assert.Equal(0, cell.Value);
    }

    [Fact]
    public void UndoRedo_EffectsOfTheStep_DoNotRunAgain()
    {
        var cell = new RewindCell<int>(0);
        var log = new List<string>();
        var history = new RewindHistory();

        history.Run(scope =>
        {
            cell.Value = 1;
            scope.Effect(() => log.Add("do"), () => log.Add("undo"));
            scope.AfterCommit(() => log.Add("after commit"));
            scope.AfterRollback(() => log.Add("after rollback"));
        });
        history.Undo();
        history.Redo();

        Assert.Equal(new[] { "do", "after commit" }, log);
        Assert.Equal(1, cell.Value);
    }
}

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Threading;

namespace ViShap.Rewind.Integration.Tests.Ui;

/// <summary>
/// A <see cref="TextBlock"/> bound to a <c>[Rewindable]</c> view model, on Avalonia's UI thread. What the screen shows
/// must follow the committed state: unchanged while a scope is open, updated once after it commits, untouched by a
/// rollback, and refreshed by undo and redo.
/// </summary>
public sealed class BindingTests : IClassFixture<AvaloniaSession>
{
    private readonly AvaloniaSession _session;

    public BindingTests(AvaloniaSession session) => _session = session;

    [Fact]
    public Task Commit_ScreenUpdatesOnceAfterTheScope_NeverWhileItIsOpen() => _session.Run(() =>
    {
        var (model, text, log) = Bound("Draft");
        string? shownInsideTheScope = null;

        Rewind.Run(() =>
        {
            model.Title = "First";
            model.Title = "Second";
            Dispatcher.UIThread.RunJobs();
            shownInsideTheScope = text.Text;
        });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Draft", shownInsideTheScope);
        Assert.Equal("Second", text.Text);
        Assert.Equal(["Title"], log);
    });

    [Fact]
    public Task Rollback_ScreenShowsTheRestoredValue_OneNotificationAfterTheRollback() => _session.Run(() =>
    {
        var (model, text, log) = Bound("Draft");

        Assert.Throws<InvalidOperationException>(() => Rewind.Run(() =>
        {
            model.Title = "Rejected";
            throw new InvalidOperationException("validation failed");
        }));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Draft", text.Text);
        Assert.Equal("Draft", model.Title);
        Assert.Equal(["Title"], log);
    });

    [Fact]
    public Task WriteWithNoScope_ScreenUpdatesAtOnce() => _session.Run(() =>
    {
        var (model, text, log) = Bound("Draft");

        model.Title = "Typed";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Typed", text.Text);
        Assert.Equal(["Title"], log);
    });

    [Fact]
    public Task UndoAndRedo_RefreshTheScreen() => _session.Run(() =>
    {
        var (model, text, log) = Bound("Draft");
        var history = new RewindHistory();

        history.Run(() => model.Title = "Edited");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Edited", text.Text);

        history.Undo();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Draft", text.Text);
        Assert.True(history.CanRedo);

        history.Redo();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Edited", text.Text);
        Assert.Equal(["Title", "Title", "Title"], log);
    });

    /// <summary>
    /// The control itself wrote the value into the model inside the scope; after the rollback it shows the restored one.
    /// </summary>
    [Fact]
    public Task TwoWayBinding_UserInputInsideARolledBackScope_ControlShowsTheRestoredValue() => _session.Run(() =>
    {
        var model = new NoteViewModel { Title = "Draft" };
        var box = new TextBox { DataContext = model };
        box.Bind(TextBox.TextProperty, new Binding(nameof(NoteViewModel.Title)) { Mode = BindingMode.TwoWay });
        var window = new Window { Content = box };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        using (Rewind.Begin())
        {
            box.Text = "Typed by the user";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Typed by the user", model.Title);
        }

        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Draft", model.Title);
        Assert.Equal("Draft", box.Text);
        window.Close();
    });

    [Fact]
    public Task ListBoundToRewindList_FollowsChangesCommitsAndRollbacks() => _session.Run(() =>
    {
        var lines = new RewindList<string>(["pen"]);
        var items = new ItemsControl { ItemsSource = lines };
        var window = new Window { Content = items };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, items.ItemCount);

        lines.Add("book");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, items.ItemCount);

        int shownInsideTheScope;
        using (var scope = Rewind.Begin())
        {
            lines.Add("lamp");
            lines.RemoveAt(0);
            Dispatcher.UIThread.RunJobs();
            shownInsideTheScope = items.ItemCount;
            scope.Commit();
        }

        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, shownInsideTheScope);
        Assert.Equal(["book", "lamp"], items.Items.Cast<string>());

        Assert.Throws<InvalidOperationException>(() => Rewind.Run(() =>
        {
            lines.Clear();
            lines.Add("rejected");
            throw new InvalidOperationException("validation failed");
        }));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["book", "lamp"], items.Items.Cast<string>());
        window.Close();
    });

    [Fact]
    public Task SetAndDictionaryBound_ShowTheirContentAndDoNotThrow() => _session.Run(() =>
    {
        var tags = new RewindSet<string>(["a", "b"]);
        var stock = new RewindDictionary<string, int> { ["pen"] = 1 };
        var tagList = new ItemsControl { ItemsSource = tags };
        var stockList = new ItemsControl { ItemsSource = stock };
        var window = new Window { Content = new StackPanel { Children = { tagList, stockList } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Rewind.Run(() =>
        {
            tags.Add("c");
            stock["book"] = 2;
        });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, tagList.ItemCount);
        Assert.Equal(1, stockList.ItemCount);
        window.Close();
    });

    [Fact]
    public Task UndoAndRedoOfAList_RefreshTheScreen() => _session.Run(() =>
    {
        var lines = new RewindList<string>();
        var history = new RewindHistory();
        var items = new ItemsControl { ItemsSource = lines };
        var window = new Window { Content = items };
        window.Show();

        history.Run(() => lines.Add("milk"));
        history.Run(() => lines.Add("bread"));
        history.Undo();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["milk"], items.Items.Cast<string>());

        history.Redo();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["milk", "bread"], items.Items.Cast<string>());
        window.Close();
    });

    private static (NoteViewModel Model, TextBlock Text, List<string> Log) Bound(string title)
    {
        var model = new NoteViewModel { Title = title };
        var log = new List<string>();
        model.PropertyChanged += (_, e) => log.Add(e.PropertyName!);

        var text = new TextBlock { DataContext = model };
        text.Bind(TextBlock.TextProperty, new Binding(nameof(NoteViewModel.Title)));
        var window = new Window { Content = text };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(title, text.Text);
        return (model, text, log);
    }
}

/// <summary>A view model as an MVVM application writes it: tracked properties, notifying the UI.</summary>
[Rewindable]
public partial class NoteViewModel : INotifyPropertyChanged
{
    public partial string Title { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>One headless Avalonia application for the test class, with its UI thread.</summary>
public sealed class AvaloniaSession : IDisposable
{
    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaSession));

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

    /// <summary>Runs <paramref name="test"/> on the UI thread.</summary>
    public Task Run(Action test) => _session.Dispatch(test, CancellationToken.None);

    public void Dispose() => _session.Dispose();
}

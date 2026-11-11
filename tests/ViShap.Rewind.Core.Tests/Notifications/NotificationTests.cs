using ViShap.Rewind.Tests.Fixtures;

namespace ViShap.Rewind.Tests.Notifications;

public sealed class NotificationTests
{
    private static readonly Counter s_counter = CreateCounter();
    private static int s_raised;

    [Fact]
    public void Commit_RaisesOncePerChangedPropertyAfterOutermostCommit()
    {
        var document = new Document { Title = "Draft", Pages = 1 };
        var log = new NotificationLog(document, name => name == "Title" ? document.Title : document.Pages);

        Rewind.Run(() =>
        {
            document.Title = "One";
            document.Pages = 2;
            Rewind.Run(() => document.Title = "Two");
            document.Pages = 3;
            Assert.Empty(log.Names);
        });

        Assert.Equal(new[] { "Title", "Pages" }, log.Names);
        Assert.Equal(new object?[] { "Two", 3 }, log.Values);
        Assert.All(log.Scopes, Assert.Null);
    }

    [Fact]
    public void Rollback_RaisesNothing()
    {
        var document = new Document { Title = "Draft" };
        var log = new NotificationLog(document);

        Assert.Throws<InvalidOperationException>(() => Rewind.Run(() =>
        {
            document.Title = "Lost";
            throw new InvalidOperationException();
        }));
        Rewind.Run(() =>
        {
            Assert.Throws<InvalidOperationException>(() => Rewind.Run(() =>
            {
                document.Pages = 9;
                throw new InvalidOperationException();
            }));
        });

        Assert.Empty(log.Names);
        Assert.Equal("Draft", document.Title);
    }

    [Fact]
    public void Commit_ValueBroughtBack_RaisesNothing()
    {
        var document = new Document { Title = "Draft" };
        var log = new NotificationLog(document);

        Rewind.Run(() =>
        {
            document.Title = "Other";
            document.Title = "Draft";
        });

        Assert.Empty(log.Names);
    }

    [Fact]
    public void Write_WithNoScope_RaisesAtOnceOnlyWhenValueChanges()
    {
        var counter = new Counter();
        var log = new NotificationLog(counter);

        counter.Count = 1;
        counter.Count = 1;
        counter.Count = 2;

        Assert.Equal(new[] { "Count", "Count" }, log.Names);
    }

    [Fact]
    public void Write_WithNoScopeToNotifyingProperty_AllocatesNothing()
    {
        var bytes = Allocations.Measure(static () =>
        {
            s_counter.Count++;
            s_counter.Count++;
        });

        Assert.Equal(0, bytes);
        Assert.True(s_raised > 0);
    }

    [Fact]
    public void Commit_HandlerThrows_StateStaysCommittedOthersRunAndEffectExceptionThrown()
    {
        var document = new Document { Title = "Draft", Pages = 1 };
        var raised = new List<string>();
        var observed = 0;
        var broken = new InvalidOperationException("handler");
        document.PropertyChanged += (_, e) =>
        {
            raised.Add(e.PropertyName!);
            if (e.PropertyName == "Title")
            {
                throw broken;
            }
        };

        var thrown = Assert.Throws<RewindEffectException>(() => Rewind.Run(scope =>
        {
            document.Title = "New";
            document.Pages = 2;
            scope.OnCommitted(_ => throw new InvalidOperationException("observer"));
            scope.OnCommitted(_ => observed++);
        }));

        Assert.Equal(new[] { "Title", "Pages" }, raised);
        Assert.Equal(1, observed);
        Assert.Equal(2, thrown.Failures.Count);
        Assert.Same(broken, thrown.Failures[0]);
        Assert.Equal("New", document.Title);
        Assert.Equal(2, document.Pages);
    }

    [Fact]
    public void Commit_FieldLikeEvent_RaisesWithCommittedValue()
    {
        var counter = new Counter();
        var log = new NotificationLog(counter, _ => counter.Count);

        Rewind.Run(() =>
        {
            counter.Count = 5;
            counter.Count = 6;
        });

        Assert.Equal(new[] { "Count" }, log.Names);
        Assert.Equal(new object?[] { 6 }, log.Values);
    }

    [Fact]
    public void Commit_HandlerCommitsAnotherNotifyingChange_BothAreRaised()
    {
        var document = new Document { Title = "Draft" };
        var counter = new Counter();
        var counted = new NotificationLog(counter);
        document.PropertyChanged += (_, _) => Rewind.Run(() => counter.Count++);
        var titled = new NotificationLog(document);

        Rewind.Run(() => document.Title = "One");
        Rewind.Run(() => document.Title = "Two");

        Assert.Equal(new[] { "Title", "Title" }, titled.Names);
        Assert.Equal(new[] { "Count", "Count" }, counted.Names);
        Assert.Equal(2, counter.Count);
    }

    private static Counter CreateCounter()
    {
        var counter = new Counter();
        counter.PropertyChanged += static (_, _) => s_raised++;
        return counter;
    }
}

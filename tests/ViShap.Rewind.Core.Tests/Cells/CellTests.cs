using ViShap.Rewind.Tests.Fixtures;

namespace ViShap.Rewind.Tests.Cells;

public sealed class CellTests
{
    private static readonly RewindCell<int> s_valueCell = new(0);
    private static readonly RewindCell<string> s_referenceCell = new("a");

    [Fact]
    public void Write_ValueTypeWithNoScope_AllocatesNothing()
    {
        var bytes = Allocations.Measure(static () =>
        {
            for (var i = 0; i < 100; i++)
            {
                s_valueCell.Value = i;
            }
        });

        Assert.Equal(0, bytes);
    }

    [Fact]
    public void Write_ReferenceTypeWithNoScope_AllocatesNothing()
    {
        var bytes = Allocations.Measure(static () =>
        {
            s_referenceCell.Value = "b";
            s_referenceCell.Value = "a";
        });

        Assert.Equal(0, bytes);
    }

    [Fact]
    public void Write_SeveralTimesInOneScope_RecordsOneEntry()
    {
        var cell = new RewindCell<int>(0);
        using var scope = Rewind.Begin();

        cell.Value = 1;
        cell.Value = 2;
        cell.Value = 3;

        Assert.Equal(1, scope.Journal.Count);
        scope.Commit();
    }

    [Fact]
    public void Write_InNestedScope_RecordsAgain()
    {
        var cell = new RewindCell<int>(0);
        using var outer = Rewind.Begin();
        cell.Value = 1;

        using (var inner = Rewind.Begin())
        {
            cell.Value = 2;
            cell.Value = 3;

            Assert.Equal(2, inner.Journal.Count);
            inner.Commit();
        }

        outer.Commit();
    }

    [Fact]
    public void Write_ValueCellAlreadyHolds_IsRecorded()
    {
        var cell = new RewindCell<int>(5);
        using var scope = Rewind.Begin();

        cell.Value = 5;

        Assert.Equal(1, scope.Journal.Count);
        scope.Commit();
    }

    [Fact]
    public void Read_InScope_RecordsNothing()
    {
        var cell = new RewindCell<int>(5);
        using var scope = Rewind.Begin();

        var value = cell.Value;

        Assert.Equal(5, value);
        Assert.Equal(0, scope.Journal.Count);
        scope.Commit();
    }

    [Fact]
    public void Rollback_ReferenceType_RestoresReferenceButNotObjectContent()
    {
        var original = new List<int> { 1 };
#pragma warning disable RWD1004 // The test proves that only the reference of a mutable element is restored.
        var cell = new RewindCell<List<int>>(original);
#pragma warning restore RWD1004

        using (Rewind.Begin())
        {
            cell.Value.Add(2);
            cell.Value = [9];
        }

        Assert.Same(original, cell.Value);
        Assert.Equal(new[] { 1, 2 }, original);
    }

    [Fact]
    public void ToString_NullValue_ReturnsEmpty()
    {
        var cell = new RewindCell<string?>(null);

        Assert.Equal(string.Empty, cell.ToString());
    }

    [Fact]
    public void ToString_Value_ReturnsValueText()
    {
        var cell = new RewindCell<string>("text");

        Assert.Equal("text", cell.ToString());
    }
}

using ViShap.Rewind.Testing;

namespace ViShap.Rewind.Tests.Verify;

public sealed class RetriesTests
{
    [Fact]
    public void Retries_RepeatableOperation_Passes()
    {
        var cell = new RewindCell<int>(0);
        var list = new RewindList<string>();

        RewindVerify.Retries(
            () => Rewind.Run(() =>
            {
                cell.Value += 1;
                list.Add("x");
                return cell.Value;
            }),
            3,
            () => (cell.Value, list.Count));

        Assert.Equal(0, cell.Value);
        Assert.Empty(list);
    }

    [Fact]
    public void Retries_ResultDependsOnUntrackedCounter_Reported()
    {
        var cell = new RewindCell<int>(0);
        var calls = 0;

        var exception = Assert.Throws<RewindVerificationException>(() => RewindVerify.Retries(
            () => Rewind.Run(() =>
            {
                cell.Value = 1;
                return ++calls;
            }),
            3,
            () => cell.Value));

        Assert.Contains("Attempt 2 disagrees", exception.Message);
        Assert.Contains("returned 2, the first attempt returned 1", exception.Message);
    }

    [Fact]
    public void Retries_PointsDiffer_Reported()
    {
        var cell = new RewindCell<int>(0);
        var list = new RewindList<string>();
        var toggle = false;

        var exception = Assert.Throws<RewindVerificationException>(() => RewindVerify.Retries(
            () => Rewind.Run(() =>
            {
                if (toggle)
                {
                    list.Add("x");
                }
                else
                {
                    cell.Value = 1;
                }

                toggle = !toggle;
                return 0;
            }),
            2,
            () => 0));

        Assert.Contains("passed change of RewindList<String> at failure point 1, the first attempt write to RewindCell<Int32>", exception.Message);
    }

    [Fact]
    public void Retries_RollbackLeavesUntrackedState_Reported()
    {
        var untracked = 0;

        var exception = Assert.Throws<RewindVerificationException>(() => RewindVerify.Retries(
            () => ++untracked,
            2,
            () => untracked));

        Assert.Contains("Attempt 1 left state", exception.Message);
    }
}

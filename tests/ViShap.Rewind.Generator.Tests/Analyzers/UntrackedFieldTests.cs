using ViShap.Rewind.Generator.Analyzers;
using ViShap.Rewind.Generator.Tests.Fixtures;

namespace ViShap.Rewind.Generator.Tests.Analyzers;

public sealed class UntrackedFieldTests
{
    [Fact]
    public void RWD1005_FieldOfThisWrittenInsideScope_ReportedAtTarget()
    {
        var diagnostics = AnalyzerHarness.Diagnostics(new UntrackedFieldAnalyzer(), """
            using ViShap.Rewind;

            [Rewindable]
            public partial class Account
            {
                private decimal _fees;
                private int _operations;
                private string? _note;

                public partial decimal Balance { get; set; }

                public void Charge(decimal amount)
                {
                    Rewind.Run(() =>
                    {
                        Balance -= amount;
                        _fees += 1m;
                        _operations++;
                        this._note = "charged";
                        _note ??= "x";
                    });
                }

                public void Refund(decimal amount)
                {
                    using var scope = Rewind.Begin();
                    Balance += amount;
                    _operations--;
                    scope.Commit();
                }
            }
            """);

        Assert.Equal(new[] { "_fees", "_operations", "this._note", "_note", "_operations" }, AnalyzerHarness.Texts(diagnostics));
        Assert.All(diagnostics, d => Assert.Equal("RWD1005", d.Id));
        Assert.Contains("_fees", diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void RWD1005_TrackedOrOutsideOrOtherObject_NotReported()
    {
        var diagnostics = AnalyzerHarness.Diagnostics(new UntrackedFieldAnalyzer(), """
            using ViShap.Rewind;

            public sealed class Ledger
            {
                public int Entries;
            }

            [Rewindable]
            public partial class Account
            {
                private int _count;
                private static int s_total;
                private readonly RewindCell<int> _cell = new();

                public partial decimal Balance { get; set; }

                public void Charge(Ledger ledger, Account other)
                {
                    _count = 1;
                    Rewind.Run(scope =>
                    {
                        var local = 0;
                        local++;
                        Balance -= 1m;
                        _cell.Value = 2;
                        s_total++;
                        ledger.Entries++;
                        other._count = 3;
                        scope.AfterCommit(() => _count++);
                        scope.AfterRollback(() => _count = 0);
                        scope.Effect(() => _count++, () => _count--);
                    });
                }
            }

            public sealed class Plain
            {
                private int _count;

                public void Run() => Rewind.Run(() => _count++);
            }
            """);

        Assert.Empty(diagnostics);
    }
}

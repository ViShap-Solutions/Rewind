using ViShap.Rewind.Generator.Analyzers;
using ViShap.Rewind.Generator.CodeFixes;
using ViShap.Rewind.Generator.Tests.Fixtures;

namespace ViShap.Rewind.Generator.Tests.Analyzers;

public sealed class EffectInScopeTests
{
    [Fact]
    public void RWD1002_EveryMutatingCallInsideRun_Reported()
    {
        var diagnostics = AnalyzerHarness.Diagnostics(new EffectInScopeAnalyzer(), """
            using System.Diagnostics;
            using System.IO;
            using System.Net.Http;
            using System.Net.Mail;
            using System.Net.Sockets;
            using ViShap.Rewind;

            public static class Shop
            {
                public static void Run(HttpClient http, SmtpClient smtp, Socket socket)
                {
                    Rewind.Run(() =>
                    {
                        File.WriteAllText("a.txt", "x");
                        File.Delete("b.txt");
                        File.Move("c.txt", "d.txt");
                        _ = http.PostAsync("https://example.org", null);
                        _ = http.GetStringAsync("https://example.org");
                        smtp.Send("a@example.org", "b@example.org", "s", "b");
                        socket.Send(new byte[1]);
                        Process.Start("tool");
                    });
                }
            }
            """);

        Assert.Equal(8, diagnostics.Length);
        Assert.All(diagnostics, d => Assert.Equal("RWD1002", d.Id));
        Assert.Contains("File.WriteAllText", diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void RWD1002_InsideBeginRegions_Reported()
    {
        var diagnostics = AnalyzerHarness.Diagnostics(new EffectInScopeAnalyzer(), """
            using System.IO;
            using ViShap.Rewind;

            public static class Shop
            {
                public static void Declaration()
                {
                    File.Delete("before.txt");
                    using var scope = Rewind.Begin();
                    File.Delete("after.txt");
                    scope.Commit();
                }

                public static void Statement()
                {
                    using (Rewind.Begin())
                    {
                        File.Delete("inside.txt");
                    }

                    File.Delete("outside.txt");
                }

                public static void Manual()
                {
                    var scope = Rewind.Begin();
                    try
                    {
                        File.Delete("manual.txt");
                    }
                    finally
                    {
                        scope.Dispose();
                    }
                }
            }
            """);

        Assert.Equal(new[] { "File.Delete(\"after.txt\")", "File.Delete(\"inside.txt\")", "File.Delete(\"manual.txt\")" }, AnalyzerHarness.Texts(diagnostics));
    }

    [Fact]
    public void RWD1002_ReadsOutsideScopesAndRegisteredEffects_NotReported()
    {
        var diagnostics = AnalyzerHarness.Diagnostics(new EffectInScopeAnalyzer(), """
            using System.IO;
            using ViShap.Rewind;

            public static class Shop
            {
                public static void Run()
                {
                    File.WriteAllText("outside.txt", "x");
                    Rewind.Run(scope =>
                    {
                        _ = File.ReadAllText("config.json");
                        _ = File.Exists("config.json");
                        using var stream = File.OpenRead("config.json");
                        scope.AfterCommit(() => File.WriteAllText("receipt.txt", "x"));
                        scope.AfterRollback(() => File.Delete("draft.txt"));
                        scope.Effect(() => File.Copy("a", "b"), () => File.Delete("b"));
                    });
                }

                private static void Helper() => File.Delete("helper.txt");
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void RWD1002_Fix_WrapsInAfterCommitOfRunScope()
    {
        var fixedSource = AnalyzerHarness.Fix(new EffectInScopeAnalyzer(), new WrapInAfterCommitCodeFix(), """
            using System.IO;
            using ViShap.Rewind;

            public static class Shop
            {
                public static void Run(RewindCell<int> cell)
                {
                    Rewind.Run(scope =>
                    {
                        cell.Value = 1;
                        File.WriteAllText("receipt.txt", "paid");
                    });
                }
            }
            """);

        Assert.Equal("""
            using System.IO;
            using ViShap.Rewind;

            public static class Shop
            {
                public static void Run(RewindCell<int> cell)
                {
                    Rewind.Run(scope =>
                    {
                        cell.Value = 1;
                        scope.AfterCommit(() => File.WriteAllText("receipt.txt", "paid"));
                    });
                }
            }
            """, fixedSource);
        Assert.Empty(GeneratorHarness.Run(fixedSource!).CompilationProblems);
        Assert.Empty(AnalyzerHarness.Diagnostics(new EffectInScopeAnalyzer(), fixedSource!));
    }

    [Fact]
    public void RWD1002_Fix_UsesBeginLocal()
    {
        var fixedSource = AnalyzerHarness.Fix(new EffectInScopeAnalyzer(), new WrapInAfterCommitCodeFix(), """
            using System.IO;
            using ViShap.Rewind;

            public static class Shop
            {
                public static void Run()
                {
                    using var transaction = Rewind.Begin();
                    File.Delete("draft.txt");
                    transaction.Commit();
                }
            }
            """);

        Assert.Contains("transaction.AfterCommit(() => File.Delete(\"draft.txt\"));", fixedSource);
        Assert.Empty(GeneratorHarness.Run(fixedSource!).CompilationProblems);
    }

    [Fact]
    public void RWD1002_Fix_NotOfferedWithoutScopeVariable()
    {
        var fixedSource = AnalyzerHarness.Fix(new EffectInScopeAnalyzer(), new WrapInAfterCommitCodeFix(), """
            using System.IO;
            using ViShap.Rewind;

            public static class Shop
            {
                public static void Run() => Rewind.Run(() => File.Delete("draft.txt"));
            }
            """);

        Assert.Null(fixedSource);
    }
}

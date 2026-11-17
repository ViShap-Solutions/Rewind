using ViShap.Rewind.Tests.Fixtures;
using ViShap.Rewind.Tests.Observability;

namespace ViShap.Rewind.Tests.Api;

/// <summary>Runs alone: an example may subscribe a listener to the whole process.</summary>
[Collection(TelemetryCollection.Name)]
public sealed class DocumentationExampleTests
{
    [Fact]
    public void Pages_EveryPageWithCode_IsFound()
    {
        var pages = CodeExamples.All().Select(example => example.Page).ToHashSet();

        Assert.Contains("docs/scopes.md", pages);
        Assert.Contains("docs/effects.md", pages);
        Assert.Contains("docs/exceptions.md", pages);
        Assert.Contains("docs/collections.md", pages);
        Assert.Contains("docs/rewindable-types.md", pages);
        Assert.Contains("README.md", pages);
        Assert.Contains("src/ViShap.Rewind.Core/CORE-README.md", pages);
        Assert.Contains("src/ViShap.Rewind/METAPACK-README.md", pages);
    }

    [Fact]
    public void Examples_EveryCsharpBlock_Compiles()
    {
        var failures = new List<string>();

        foreach (var example in CodeExamples.All())
        {
            var (_, problems) = CodeExamples.Compile(example);
            failures.AddRange(problems.Select(example.Describe));
        }

        Assert.True(failures.Count == 0, "Documentation examples do not compile:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Examples_EveryTopLevelBlock_RunsToCompletion()
    {
        var failures = new List<string>();

        foreach (var example in CodeExamples.All())
        {
            var (assembly, problems) = CodeExamples.Compile(example);
            if (assembly is null || problems.Count > 0 || !IsProgram(assembly))
            {
                continue;
            }

            if (CodeExamples.Run(assembly) is { } exception)
            {
                failures.Add($"{example.Location}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        Assert.True(failures.Count == 0, "Documentation examples fail when run:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
        Assert.Null(Rewind.Current);
    }

    /// <summary>
    /// What an example prints is what the page says it prints: every line written to the console appears, in the order
    /// it was written, as a comment of the example — trailing a line of code or on a line of its own. A comment may go
    /// on after the printed text (<c>// 100 0, and no receipt was sent</c>); comments that explain rather than show
    /// output are allowed anywhere.
    /// </summary>
    [Fact]
    public void Examples_EveryTopLevelBlock_PrintsWhatItsCommentsSay()
    {
        var failures = new List<string>();

        foreach (var example in CodeExamples.All())
        {
            var (assembly, problems) = CodeExamples.Compile(example);
            if (assembly is null || problems.Count > 0 || !IsProgram(assembly))
            {
                continue;
            }

            var output = new StringWriter();
            if (CodeExamples.Run(assembly, output) is not null)
            {
                continue;
            }

            var comments = example.Code.Split('\n')
                .Select(line => line.IndexOf("//", StringComparison.Ordinal) is var at and >= 0 ? line[(at + 2)..].Trim() : null)
                .OfType<string>()
                .ToList();
            var printed = output.ToString().ReplaceLineEndings("\n").Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0);
            var position = 0;

            foreach (var line in printed)
            {
                var found = comments.FindIndex(position, comment => comment == line || comment.StartsWith(line + ",", StringComparison.Ordinal) || comment.StartsWith(line + "  ", StringComparison.Ordinal));
                if (found < 0)
                {
                    failures.Add($"{example.Location}: prints \"{line}\", which no comment after the previous output shows");
                    break;
                }

                position = found + 1;
            }
        }

        Assert.True(failures.Count == 0, "Documentation examples print what their comments do not say:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    private static bool IsProgram(byte[] assembly)
    {
        using var stream = new MemoryStream(assembly);
        using var reader = new System.Reflection.PortableExecutable.PEReader(stream);
        return reader.PEHeaders.CorHeader?.EntryPointTokenOrRelativeVirtualAddress != 0;
    }
}

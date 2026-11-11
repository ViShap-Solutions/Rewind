using ViShap.Rewind.Tests.Fixtures;

namespace ViShap.Rewind.Tests.Api;

public sealed class DocumentationExampleTests
{
    [Fact]
    public void Pages_EveryPageWithCode_IsFound()
    {
        var pages = CodeExamples.All().Select(example => example.Page).ToHashSet();

        Assert.Contains("docs/scopes.md", pages);
        Assert.Contains("docs/effects.md", pages);
        Assert.Contains("docs/exceptions.md", pages);
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

    private static bool IsProgram(byte[] assembly)
    {
        using var stream = new MemoryStream(assembly);
        using var reader = new System.Reflection.PortableExecutable.PEReader(stream);
        return reader.PEHeaders.CorHeader?.EntryPointTokenOrRelativeVirtualAddress != 0;
    }
}

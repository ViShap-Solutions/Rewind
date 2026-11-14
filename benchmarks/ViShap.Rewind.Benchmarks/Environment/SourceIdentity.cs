using System.Text.RegularExpressions;

namespace ViShap.Rewind.Benchmarks.Environment;

/// <summary>
/// Whether the library at <c>HEAD</c> is the library a tag released, so a run on a later commit may be recorded as
/// that tag's baseline. Only what cannot reach a compiled instruction may differ under <c>src/</c>: a Markdown file,
/// and in a project file a package metadata property, which ends up in the package or in an assembly attribute and
/// in no method body.
/// </summary>
internal static partial class SourceIdentity
{
    private const string Source = "src";

    /// <summary>The outcome: the files that differ at all, and among them the ones that may change code.</summary>
    internal sealed record Comparison(IReadOnlyList<string> Differing, IReadOnlyList<string> Code)
    {
        internal bool SameCode => Code.Count == 0;
    }

    /// <summary>Compares <c>src/</c> between the committed tree of <paramref name="tag"/> and of <c>HEAD</c>.</summary>
    internal static Comparison Compare(string tag)
    {
        var differing = Lines(Shell.Git($"diff --name-only {tag} HEAD -- \":(top){Source}\""));
        var code = differing.Where(file => !MayDiffer(tag, file)).ToList();

        return new Comparison(differing, code);
    }

    /// <summary>Whether the tag exists in this repository, so a typing error is not read as a difference.</summary>
    internal static bool TagExists(string tag) =>
        !string.IsNullOrEmpty(Shell.Git($"rev-parse --verify --quiet refs/tags/{tag}"));

    private static bool MayDiffer(string tag, string file)
    {
        if (file.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Every changed line, removed or added, must be one package metadata property; an empty diff means git
        // could not answer, which is not evidence that nothing changed.
        var changed = Lines(Shell.Git($"diff -U0 {tag} HEAD -- \":(top){file}\""))
            .Where(line => line.Length > 0 && line[0] is '+' or '-')
            .Where(line => !line.StartsWith("+++", StringComparison.Ordinal) && !line.StartsWith("---", StringComparison.Ordinal))
            .Select(line => line[1..])
            .ToList();

        return changed.Count > 0 && changed.All(line => string.IsNullOrWhiteSpace(line) || Metadata().IsMatch(line));
    }

    private static List<string> Lines(string text) =>
        [.. text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0)];

    [GeneratedRegex(
        @"^\s*<(Copyright|Authors|Company|Product|Title|Description|PackageTags|PackageProjectUrl|PackageReleaseNotes|PackageIcon|PackageReadmeFile|PackageLicenseExpression|PackageLicenseFile|RepositoryUrl|RepositoryType)>[^<]*</\1>\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Metadata();
}

using System.Reflection;
using System.Xml.Linq;
using ViShap.Rewind.Tests.Fixtures;

namespace ViShap.Rewind.Tests.Api;

public sealed class PublicSurfaceTests
{
    private const string ListingPath = "tests/ViShap.Rewind.Core.Tests/Api/PublicSurface.Core.txt";

    private static readonly Assembly s_core = typeof(Rewind).Assembly;

    [Fact]
    public void Core_ExportedSurface_EqualsListing()
    {
        var expected = Entries(File.ReadAllLines(Repository.PathOf(ListingPath)));

        var actual = Entries(PublicApi.Render(s_core));

        var unexpected = actual.Except(expected).ToList();
        var missing = expected.Except(actual).ToList();
        Assert.True(
            unexpected.Count == 0 && missing.Count == 0,
            $"The exported surface of {s_core.GetName().Name} differs from {ListingPath}."
                + Section("Exported but not listed", unexpected)
                + Section("Listed but not exported", missing));
    }

    [Fact]
    public void Core_EveryExportedMember_IsDocumented()
    {
        var documentation = XDocument.Load(Path.ChangeExtension(s_core.Location, ".xml"))
            .Descendants("member")
            .ToDictionary(member => (string)member.Attribute("name")!);
        var problems = new List<string>();

        foreach (var type in s_core.GetExportedTypes())
        {
            Check(DocumentationIds.Of(type), [], type.IsGenericTypeDefinition ? type.GetGenericArguments() : [], returns: false);

            foreach (var member in VisibleMembers(type))
            {
                switch (member)
                {
                    case PropertyInfo property:
                        Check(DocumentationIds.Of(property), [], [], returns: false);
                        break;
                    case MethodInfo method:
                        Check(DocumentationIds.Of(method), method.GetParameters(), method.IsGenericMethodDefinition ? method.GetGenericArguments() : [], returns: method.ReturnType != typeof(void));
                        break;
                    case ConstructorInfo constructor:
                        Check(DocumentationIds.Of(constructor), constructor.GetParameters(), [], returns: false);
                        break;
                }
            }
        }

        Assert.True(problems.Count == 0, "XML documentation is incomplete:" + Section(string.Empty, problems));

        void Check(string id, ParameterInfo[] parameters, Type[] typeParameters, bool returns)
        {
            if (!documentation.TryGetValue(id, out var member))
            {
                problems.Add($"{id}: no documentation");
                return;
            }

            if (member.Element("inheritdoc") is not null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(member.Element("summary")?.Value))
            {
                problems.Add($"{id}: no <summary>");
            }

            foreach (var parameter in parameters.Where(parameter => !member.Elements("param").Any(element => (string?)element.Attribute("name") == parameter.Name)))
            {
                problems.Add($"{id}: no <param name=\"{parameter.Name}\">");
            }

            foreach (var typeParameter in typeParameters.Where(typeParameter => !member.Elements("typeparam").Any(element => (string?)element.Attribute("name") == typeParameter.Name)))
            {
                problems.Add($"{id}: no <typeparam name=\"{typeParameter.Name}\">");
            }

            if (returns && string.IsNullOrWhiteSpace(member.Element("returns")?.Value))
            {
                problems.Add($"{id}: no <returns>");
            }
        }
    }

    private static IEnumerable<MemberInfo> VisibleMembers(Type type)
    {
        const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        static bool Visible(MethodBase method) => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

        foreach (var constructor in type.GetConstructors(declared).Where(Visible))
        {
            yield return constructor;
        }

        foreach (var property in type.GetProperties(declared).Where(property => property.GetAccessors(true).Any(Visible)))
        {
            yield return property;
        }

        foreach (var method in type.GetMethods(declared).Where(method => Visible(method) && !method.IsSpecialName))
        {
            yield return method;
        }
    }

    private static HashSet<string> Entries(IEnumerable<string> lines)
    {
        var entries = new HashSet<string>(StringComparer.Ordinal);
        var header = string.Empty;
        foreach (var line in lines.Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            if (line.StartsWith(' '))
            {
                entries.Add($"{header} :: {line.Trim()}");
            }
            else
            {
                header = line.Trim();
                entries.Add(header);
            }
        }

        return entries;
    }

    private static string Section(string title, List<string> lines) =>
        lines.Count == 0
            ? string.Empty
            : $"{Environment.NewLine}{title}{(title.Length == 0 ? string.Empty : ":")}{Environment.NewLine}  "
                + string.Join(Environment.NewLine + "  ", lines.OrderBy(line => line, StringComparer.Ordinal));
}

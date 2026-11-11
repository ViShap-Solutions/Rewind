using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ViShap.Rewind.Tests.Structure;

public sealed class StructureTests
{
    private static readonly string[] s_ioNamespaces = ["System.IO", "System.Net", "Microsoft.Win32"];

    private static readonly string[] s_ioTypes = ["System.Console", "System.Diagnostics.Process"];

    [Fact]
    public void CoreAssembly_ReferencesNoInputOutputType()
    {
        var referenced = ReferencedTypeNames(typeof(Rewind).Assembly.Location);

        var offending = referenced
            .Where(name => s_ioTypes.Contains(name)
                || s_ioNamespaces.Any(ns => name.StartsWith(ns + ".", StringComparison.Ordinal)))
            .ToList();

        Assert.NotEmpty(referenced);
        Assert.Empty(offending);
    }

    private static List<string> ReferencedTypeNames(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();
        var names = new List<string>();

        foreach (var handle in metadata.TypeReferences)
        {
            var type = metadata.GetTypeReference(handle);
            var name = metadata.GetString(type.Name);
            var ns = metadata.GetString(type.Namespace);
            if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                var outer = metadata.GetTypeReference((TypeReferenceHandle)type.ResolutionScope);
                ns = metadata.GetString(outer.Namespace);
            }

            names.Add(ns.Length == 0 ? name : ns + "." + name);
        }

        return names;
    }
}

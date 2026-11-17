using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ViShap.Rewind.Generator;

/// <summary>The rules about types that the generator and the analyzers share.</summary>
internal static class TypeRules
{
    /// <summary>The metadata name of <c>[Rewindable]</c>.</summary>
    public const string RewindableAttribute = "ViShap.Rewind.RewindableAttribute";

    private static readonly ImmutableHashSet<string> s_rewindCollections = ImmutableHashSet.Create(
        "ViShap.Rewind.RewindList<T>",
        "ViShap.Rewind.RewindDictionary<TKey, TValue>",
        "ViShap.Rewind.RewindSet<T>");

    private static readonly ImmutableHashSet<string> s_mutableCollectionInterfaces = ImmutableHashSet.Create(
        "System.Collections.Generic.ICollection<T>",
        "System.Collections.ICollection",
        "System.Collections.IList",
        "System.Collections.IDictionary");

    /// <summary>Returns whether <paramref name="type"/> carries <c>[Rewindable]</c>.</summary>
    public static bool IsRewindable(ITypeSymbol type) =>
        type.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == RewindableAttribute);

    /// <summary>Returns whether <paramref name="type"/> is <c>RewindList</c>, <c>RewindDictionary</c> or <c>RewindSet</c>.</summary>
    public static bool IsRewindCollection(ITypeSymbol type) =>
        type is INamedTypeSymbol named && s_rewindCollections.Contains(named.OriginalDefinition.ToDisplayString());

    /// <summary>
    /// Returns whether <paramref name="type"/> is a reference type whose instances can change: an array, a mutable
    /// collection, or a class with a public setter or a public writable field. Only the reference to such an object is
    /// tracked.
    /// </summary>
    public static bool IsMutableReference(ITypeSymbol type)
    {
        if (type.IsValueType || type.TypeKind is TypeKind.TypeParameter or TypeKind.Delegate or TypeKind.Error
            || type.SpecialType is SpecialType.System_String or SpecialType.System_Object)
        {
            return false;
        }

        if (type.TypeKind == TypeKind.Array)
        {
            return true;
        }

        var ns = type.ContainingNamespace?.ToDisplayString();
        if (IsRewindCollection(type) || IsRewindable(type) || ns == "System.Collections.Immutable"
            || (ns == "System.Collections.ObjectModel" && type.Name.StartsWith("ReadOnly", System.StringComparison.Ordinal)))
        {
            return false;
        }

        if (IsMutableCollectionInterface(type) || type.AllInterfaces.Any(IsMutableCollectionInterface))
        {
            return true;
        }

        if (type.TypeKind == TypeKind.Interface)
        {
            return false;
        }

        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (member.IsStatic || member.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                if (member is IPropertySymbol { SetMethod: { IsInitOnly: false, DeclaredAccessibility: Accessibility.Public } }
                    || member is IFieldSymbol { IsReadOnly: false, IsConst: false })
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsMutableCollectionInterface(ITypeSymbol type) =>
        type is INamedTypeSymbol named && s_mutableCollectionInterfaces.Contains(named.OriginalDefinition.ToDisplayString());
}

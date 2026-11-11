using Microsoft.CodeAnalysis;

namespace ViShap.Rewind.Generator;

/// <summary>The diagnostics <see cref="RewindableAnalyzer"/> reports about rewindable types.</summary>
internal static class Descriptors
{
    /// <summary>The page that explains every diagnostic; each one links to its own section.</summary>
    public const string HelpRoot = "https://github.com/ViShap-Solutions/Rewind/blob/main/docs/diagnostics.md#";

    private const string Category = "ViShap.Rewind";

    public static readonly DiagnosticDescriptor NotPartial = new(
        "RWD0001",
        "Rewindable type must be partial",
        "'{0}' must be partial: the generator implements the partial properties of a rewindable type in another part of it",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpRoot + "rwd0001");

    public static readonly DiagnosticDescriptor UntrackedProperty = new(
        "RWD0002",
        "Writable property is not rolled back",
        "'{0}' is writable but not partial: a rollback does not restore it; declare it partial to track it, or mark it [RewindIgnore]",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpRoot + "rwd0002");

    public static readonly DiagnosticDescriptor UnsupportedAccessor = new(
        "RWD0003",
        "Partial property has an init accessor or a ref return",
        "'{0}' has an init accessor or returns by reference: a tracked property has a get and a set accessor",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpRoot + "rwd0003");

    public static readonly DiagnosticDescriptor MutableReference = new(
        "RWD0004",
        "Only the reference is tracked",
        "'{0}' is of the mutable type '{1}': a rollback restores which object it refers to, not changes made inside that object",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpRoot + "rwd0004");

    public static readonly DiagnosticDescriptor UnsupportedShape = new(
        "RWD0005",
        "Partial property cannot be tracked",
        "'{0}' cannot be implemented as tracked state: a tracked property is an instance property, not an indexer, with a set accessor",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpRoot + "rwd0005");
}

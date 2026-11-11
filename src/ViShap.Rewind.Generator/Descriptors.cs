using Microsoft.CodeAnalysis;

namespace ViShap.Rewind.Generator;

/// <summary>Every diagnostic of the package: RWD0xxx about rewindable types, RWD1xxx about the use of scopes.</summary>
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

    public static readonly DiagnosticDescriptor ScopeLeak = new(
        "RWD1001",
        "Scope is never disposed",
        "The scope returned by Rewind.Begin() is never disposed: it stays the current scope and records every later write; declare it with 'using'",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpRoot + "rwd1001");

    public static readonly DiagnosticDescriptor EffectInScope = new(
        "RWD1002",
        "External effect called directly inside a scope",
        "'{0}' changes something outside the process and a rollback cannot undo it; register it with AfterCommit, or with Effect and a compensation",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpRoot + "rwd1002");

    public static readonly DiagnosticDescriptor AsyncRun = new(
        "RWD1003",
        "Asynchronous block passed to Rewind.Run",
        "Rewind.Run runs its block synchronously: an asynchronous block keeps running after the scope has closed, and its writes after the first await are not tracked",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpRoot + "rwd1003");

    public static readonly DiagnosticDescriptor MutableCell = new(
        "RWD1004",
        "Only the reference is tracked",
        "RewindCell<{0}> holds a mutable reference type: a rollback restores which object it refers to, not changes made inside that object",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpRoot + "rwd1004");

    public static readonly DiagnosticDescriptor UntrackedField = new(
        "RWD1005",
        "Untracked field written inside a scope",
        "Field '{0}' is written inside a scope but is not tracked: a rollback does not restore it; move it to a partial property",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpRoot + "rwd1005");
}

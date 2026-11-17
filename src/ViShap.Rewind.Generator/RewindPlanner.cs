using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ViShap.Rewind.Generator.Model;

namespace ViShap.Rewind.Generator;

/// <summary>
/// Decides, for a rewindable type, which partial properties the generator implements and what is reported. The
/// generator takes the plan from it; <see cref="RewindableAnalyzer"/> takes the diagnostics. No rule lives elsewhere.
/// </summary>
internal static class RewindPlanner
{
    public const string RewindableAttribute = TypeRules.RewindableAttribute;

    /// <summary>The ending of the name of every file the generator writes.</summary>
    public const string GeneratedSuffix = ".Rewind.g.cs";

    private const string IgnoreAttribute = "ViShap.Rewind.RewindIgnoreAttribute";

    private static readonly SymbolDisplayFormat s_typeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>Plans the source the generator writes for <paramref name="type"/>.</summary>
    public static TypePlan Plan(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        var properties = ImmutableArray.CreateBuilder<PropertyPlan>();
        if (NotPartialDeclaration(type, cancellationToken) is null)
        {
            foreach (var property in DeclaredProperties(type))
            {
                if (Classify(property, cancellationToken) is { Implement: true } verdict)
                {
                    properties.Add(Implementation(property, verdict.Tracked, cancellationToken));
                }
            }
        }

        return new TypePlan(
            HintName(type),
            type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
            new EquatableArray<TypeDeclarationPlan>(ContainingTypes(type)),
            Declaration(type),
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            new EquatableArray<PropertyPlan>(properties.ToImmutable()),
            Notification(type, cancellationToken) ?? NotifyKind.None);
    }

    /// <summary>Returns what is reported about <paramref name="type"/>.</summary>
    public static IEnumerable<Diagnostic> Diagnose(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        if (NotPartialDeclaration(type, cancellationToken) is { } notPartial)
        {
            yield return Diagnostic.Create(Descriptors.NotPartial, notPartial.Identifier.GetLocation(), notPartial.Identifier.ValueText);
            yield break;
        }

        if (Notification(type, cancellationToken) is null)
        {
            yield return Diagnostic.Create(Descriptors.NoNotification, type.Locations.FirstOrDefault() ?? Location.None, type.Name);
        }

        foreach (var property in DeclaredProperties(type))
        {
            if (Classify(property, cancellationToken).Report is { } descriptor)
            {
                var location = property.Locations.FirstOrDefault() ?? Location.None;
                yield return descriptor == Descriptors.MutableReference
                    ? Diagnostic.Create(descriptor, location, property.Name, property.Type.ToDisplayString())
                    : Diagnostic.Create(descriptor, location, property.Name);
            }
        }
    }

    private static Verdict Classify(IPropertySymbol property, CancellationToken cancellationToken)
    {
        var ignored = property.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == IgnoreAttribute);

        if (!property.IsPartialDefinition)
        {
            var untracked = property.SetMethod is { IsInitOnly: false } && !ignored && !TypeRules.IsRewindCollection(property.Type)
                && IsAutoSetter(property, cancellationToken);
            return new Verdict(Implement: false, Tracked: false, untracked ? Descriptors.UntrackedProperty : null);
        }

        if (property.PartialImplementationPart is { } implementation && !IsGenerated(implementation))
        {
            return new Verdict(Implement: false, Tracked: false, Report: null);
        }

        if (property.IsStatic || property.IsIndexer || property.SetMethod is null)
        {
            return new Verdict(Implement: false, Tracked: false, Descriptors.UnsupportedShape);
        }

        if (property.SetMethod.IsInitOnly || property.ReturnsByRef || property.ReturnsByRefReadonly)
        {
            return new Verdict(Implement: false, Tracked: false, Descriptors.UnsupportedAccessor);
        }

        return new Verdict(Implement: true, Tracked: !ignored, !ignored && TypeRules.IsMutableReference(property.Type) ? Descriptors.MutableReference : null);
    }

    private static PropertyPlan Implementation(IPropertySymbol property, bool tracked, CancellationToken cancellationToken)
    {
        var syntax = (PropertyDeclarationSyntax)property.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken);
        var getter = syntax.AccessorList?.Accessors.FirstOrDefault(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
        var setter = syntax.AccessorList?.Accessors.FirstOrDefault(accessor => accessor.IsKind(SyntaxKind.SetAccessorDeclaration));

        return new PropertyPlan(
            property.Name,
            property.Type.ToDisplayString(s_typeFormat),
            Modifiers(syntax.Modifiers),
            getter is null ? string.Empty : Modifiers(getter.Modifiers),
            setter is null ? string.Empty : Modifiers(setter.Modifiers),
            tracked,
            ReferenceLike: !property.Type.IsValueType && property.Type.NullableAnnotation != NullableAnnotation.Annotated);
    }

    /// <summary>
    /// Returns how <paramref name="type"/> raises a change notification: <see cref="NotifyKind.None"/> when it does not
    /// implement <c>INotifyPropertyChanged</c>, or <see langword="null"/> when it does but has nothing generated code can
    /// call.
    /// </summary>
    private static string? Notification(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        if (!type.AllInterfaces.Any(candidate => candidate.ToDisplayString() == "System.ComponentModel.INotifyPropertyChanged"))
        {
            return NotifyKind.None;
        }

        string? args = null;
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers("OnPropertyChanged").OfType<IMethodSymbol>())
            {
                if (method.IsStatic || method.Parameters.Length != 1 || !Callable(method, type))
                {
                    continue;
                }

                var parameter = method.Parameters[0].Type.ToDisplayString();
                if (method.Parameters[0].Type.SpecialType == SpecialType.System_String)
                {
                    return NotifyKind.NameMethod;
                }

                if (parameter == "System.ComponentModel.PropertyChangedEventArgs")
                {
                    args ??= NotifyKind.ArgsMethod;
                }
            }
        }

        if (args is not null)
        {
            return args;
        }

        var fieldLike = type.GetMembers("PropertyChanged").OfType<IEventSymbol>().Any(candidate =>
            !candidate.IsStatic
            && candidate.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax { Parent.Parent: EventFieldDeclarationSyntax }));
        return fieldLike ? NotifyKind.Event : null;
    }

    private static bool Callable(IMethodSymbol method, INamedTypeSymbol type) =>
        SymbolEqualityComparer.Default.Equals(method.ContainingType, type)
        || method.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal
        || (method.DeclaredAccessibility is Accessibility.Internal or Accessibility.ProtectedAndInternal
            && SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, type.ContainingAssembly));

    private static IOrderedEnumerable<IPropertySymbol> DeclaredProperties(INamedTypeSymbol type) =>
        type.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(property => !property.IsImplicitlyDeclared && property.PartialDefinitionPart is null && property.Locations.Any(location => location.IsInSource))
            .OrderBy(property => property.Locations[0].SourceTree?.FilePath, System.StringComparer.Ordinal)
            .ThenBy(property => property.Locations[0].SourceSpan.Start);

    private static TypeDeclarationSyntax? NotPartialDeclaration(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            foreach (var reference in current.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax declaration
                    && !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                {
                    return declaration;
                }
            }
        }

        return null;
    }

    private static bool IsGenerated(IPropertySymbol implementation) =>
        implementation.Locations.All(location => location.SourceTree?.FilePath.EndsWith(GeneratedSuffix, System.StringComparison.Ordinal) == true);

    private static bool IsAutoSetter(IPropertySymbol property, CancellationToken cancellationToken) =>
        property.DeclaringSyntaxReferences.Length > 0
        && property.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is PropertyDeclarationSyntax { AccessorList: { } accessors }
        && accessors.Accessors.Any(accessor => accessor.IsKind(SyntaxKind.SetAccessorDeclaration) && accessor.Body is null && accessor.ExpressionBody is null);

    private static ImmutableArray<TypeDeclarationPlan> ContainingTypes(INamedTypeSymbol type)
    {
        var chain = ImmutableArray.CreateBuilder<TypeDeclarationPlan>();
        for (var current = type.ContainingType; current is not null; current = current.ContainingType)
        {
            chain.Insert(0, Declaration(current));
        }

        return chain.ToImmutable();
    }

    private static TypeDeclarationPlan Declaration(INamedTypeSymbol type)
    {
        var keyword = type.TypeKind switch
        {
            TypeKind.Struct => type.IsRecord ? "record struct" : "struct",
            TypeKind.Interface => "interface",
            _ => type.IsRecord ? "record" : "class",
        };

        var name = type.TypeParameters.Length == 0
            ? type.Name
            : $"{type.Name}<{string.Join(", ", type.TypeParameters.Select(parameter => parameter.Name))}>";
        return new TypeDeclarationPlan(keyword, name);
    }

    private static string HintName(INamedTypeSymbol type)
    {
        var parts = new List<string>();
        for (var current = type; current is not null; current = current.ContainingType)
        {
            parts.Insert(0, current.TypeParameters.Length == 0 ? current.Name : $"{current.Name}_{current.TypeParameters.Length}");
        }

        if (!type.ContainingNamespace.IsGlobalNamespace)
        {
            parts.Insert(0, type.ContainingNamespace.ToDisplayString());
        }

        return string.Join(".", parts) + GeneratedSuffix;
    }

    private static string Modifiers(SyntaxTokenList modifiers) => string.Join(" ", modifiers.Select(modifier => modifier.Text));

    private readonly record struct Verdict(bool Implement, bool Tracked, DiagnosticDescriptor? Report);
}

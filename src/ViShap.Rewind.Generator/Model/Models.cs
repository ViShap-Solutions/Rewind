namespace ViShap.Rewind.Generator.Model;

/// <summary>What the generator writes for one rewindable type.</summary>
/// <param name="HintName">The name of the generated file.</param>
/// <param name="Namespace">The namespace of the type, or <see langword="null"/> for the global namespace.</param>
/// <param name="ContainingTypes">The types the type is nested in, outermost first.</param>
/// <param name="Declaration">The partial declaration of the type itself.</param>
/// <param name="OwnerName">The fully qualified name of the type, for the restore delegates.</param>
/// <param name="Properties">The partial properties to implement, in declaration order.</param>
internal sealed record TypePlan(
    string HintName,
    string? Namespace,
    EquatableArray<TypeDeclarationPlan> ContainingTypes,
    TypeDeclarationPlan Declaration,
    string OwnerName,
    EquatableArray<PropertyPlan> Properties);

/// <summary>A partial type declaration: its keyword and its name with type parameters.</summary>
/// <param name="Keyword">The declaration keyword: <c>class</c>, <c>record</c>, <c>struct</c>, <c>record struct</c> or <c>interface</c>.</param>
/// <param name="Name">The name with its type parameters.</param>
internal sealed record TypeDeclarationPlan(string Keyword, string Name);

/// <summary>A partial property to implement.</summary>
/// <param name="Name">The property name.</param>
/// <param name="Type">The fully qualified property type, with its nullable annotation.</param>
/// <param name="Modifiers">The modifiers of the declaration, as written.</param>
/// <param name="GetterModifiers">The modifiers of the <c>get</c> accessor, as written.</param>
/// <param name="SetterModifiers">The modifiers of the <c>set</c> accessor, as written.</param>
/// <param name="Tracked"><see langword="false"/> for a property marked <c>[RewindIgnore]</c>.</param>
/// <param name="ReferenceLike"><see langword="true"/> when the storage holds a reference not annotated as nullable, so it starts as <c>default!</c>.</param>
internal sealed record PropertyPlan(
    string Name,
    string Type,
    string Modifiers,
    string GetterModifiers,
    string SetterModifiers,
    bool Tracked,
    bool ReferenceLike);

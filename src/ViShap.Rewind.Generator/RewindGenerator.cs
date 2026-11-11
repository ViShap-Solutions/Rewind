using Microsoft.CodeAnalysis;

namespace ViShap.Rewind.Generator;

/// <summary>
/// Writes the tracked implementation of the partial properties of a type marked as rewindable.
/// The pipeline is registered empty until the generator stage builds it.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class RewindGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
    }
}

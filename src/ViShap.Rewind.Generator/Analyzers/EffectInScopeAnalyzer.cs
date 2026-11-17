using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ViShap.Rewind.Generator.Analyzers;

/// <summary>
/// RWD1002: a call that changes something outside the process — writing a file, an HTTP request, sending mail or
/// socket data, starting a process — made directly inside a scope, where a rollback cannot undo it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EffectInScopeAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> s_fileWrites = ImmutableHashSet.Create(
        "WriteAllText", "WriteAllTextAsync", "WriteAllBytes", "WriteAllBytesAsync", "WriteAllLines", "WriteAllLinesAsync",
        "AppendAllText", "AppendAllTextAsync", "AppendAllBytes", "AppendAllBytesAsync", "AppendAllLines", "AppendAllLinesAsync",
        "AppendText", "Create", "CreateText", "CreateSymbolicLink", "Delete", "Move", "Copy", "Replace", "Encrypt", "Decrypt",
        "SetAttributes", "SetCreationTime", "SetCreationTimeUtc", "SetLastAccessTime", "SetLastAccessTimeUtc",
        "SetLastWriteTime", "SetLastWriteTimeUtc", "SetUnixFileMode", "OpenWrite");

    private static readonly ImmutableHashSet<string> s_socketCalls = ImmutableHashSet.Create(
        "Send", "SendAsync", "SendTo", "SendToAsync", "SendFile", "SendFileAsync", "SendPacketsAsync", "Connect", "ConnectAsync");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Descriptors.EffectInScope);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (RewindSymbols.From(start.Compilation) is not { } symbols)
            {
                return;
            }

            start.RegisterOperationAction(
                operationContext =>
                {
                    var invocation = (IInvocationOperation)operationContext.Operation;
                    if (!IsExternalEffect(invocation.TargetMethod))
                    {
                        return;
                    }

                    if (ScopeRegion.Of(invocation.Syntax, operationContext.Operation.SemanticModel!, symbols, operationContext.CancellationToken) is not null)
                    {
                        operationContext.ReportDiagnostic(Diagnostic.Create(
                            Descriptors.EffectInScope,
                            invocation.Syntax.GetLocation(),
                            $"{invocation.TargetMethod.ContainingType.Name}.{invocation.TargetMethod.Name}"));
                    }
                },
                OperationKind.Invocation);
        });
    }

    /// <summary>Returns whether <paramref name="method"/> is one of the calls RWD1002 reports.</summary>
    internal static bool IsExternalEffect(IMethodSymbol method)
    {
        var type = method.ReducedFrom?.Parameters[0].Type ?? (method.IsExtensionMethod ? method.Parameters[0].Type : method.ContainingType);
        return type.ToDisplayString() switch
        {
            "System.IO.File" => s_fileWrites.Contains(method.Name),
            "System.Net.Http.HttpClient" => method.MethodKind is MethodKind.Ordinary or MethodKind.ReducedExtension
                && (method.Name.StartsWith("Send", System.StringComparison.Ordinal)
                    || method.Name.StartsWith("Get", System.StringComparison.Ordinal)
                    || method.Name.StartsWith("Post", System.StringComparison.Ordinal)
                    || method.Name.StartsWith("Put", System.StringComparison.Ordinal)
                    || method.Name.StartsWith("Patch", System.StringComparison.Ordinal)
                    || method.Name.StartsWith("Delete", System.StringComparison.Ordinal)),
            "System.Net.Mail.SmtpClient" => method.Name.StartsWith("Send", System.StringComparison.Ordinal),
            "System.Net.Sockets.Socket" => s_socketCalls.Contains(method.Name),
            "System.Diagnostics.Process" => method.Name == "Start",
            _ => false,
        };
    }
}

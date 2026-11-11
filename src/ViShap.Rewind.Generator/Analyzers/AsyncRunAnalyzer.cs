using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ViShap.Rewind.Generator.Analyzers;

/// <summary>RWD1003: an <c>async</c> lambda, anonymous method or method passed to <c>Rewind.Run</c>.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AsyncRunAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Descriptors.AsyncRun);

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
                    if (!symbols.IsRun(invocation.TargetMethod))
                    {
                        return;
                    }

                    foreach (var argument in invocation.Arguments)
                    {
                        var value = argument.Value;
                        while (value is IConversionOperation conversion)
                        {
                            value = conversion.Operand;
                        }

                        var asynchronous = value is IDelegateCreationOperation creation && creation.Target switch
                        {
                            IAnonymousFunctionOperation function => function.Symbol.IsAsync,
                            IMethodReferenceOperation reference => reference.Method.IsAsync,
                            _ => false,
                        };

                        if (asynchronous)
                        {
                            operationContext.ReportDiagnostic(Diagnostic.Create(Descriptors.AsyncRun, argument.Value.Syntax.GetLocation()));
                        }
                    }
                },
                OperationKind.Invocation);
        });
    }
}

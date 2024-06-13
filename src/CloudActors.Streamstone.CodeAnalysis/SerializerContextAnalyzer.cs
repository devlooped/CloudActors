using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using static Devlooped.CloudActors.Diagnostics;

namespace Devlooped.CloudActors;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SerializerContextAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(JsonSerializerContextMissing);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
        context.RegisterCompilationAction(AnalyzeCompilation);
        context.RegisterOperationAction(AnalyzeOperation, OperationKind.Invocation);
    }

    void AnalyzeOperation(OperationAnalysisContext context)
    {       
        var invocation = (IInvocationOperation)context.Operation;
        if (context.Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver") is not { } resolverType ||
            invocation.Type?.Name != "StreamstoneSiloBuilderExtensions" ||
            invocation.TargetMethod.Name != "AddStreamstoneActorStorage" ||
            invocation.TargetMethod.ContainingType?.Name != "StreamstoneSiloBuilderExtensions" ||
            invocation.TargetMethod.ContainingType.ContainingAssembly.Name != "Devlooped.CloudActors.Streamstone")
            return;

        //resolverType = resolverType.WithNullableAnnotation(NullableAnnotation.Annotated);

        // Invocation would look like: AddStreamstoneActorStorage(options => options.JsonOptions.TypeInfoResolver = StreamstoneContext.Default))
        // We need to check whether the options.JsonOptions.TypeInfoResolver is set to a JsonSerializerContext-derived type.
        var resolver = invocation.Descendants().OfType<IAssignmentOperation>().FirstOrDefault(
            a => resolverType.Equals(a.Type, SymbolEqualityComparer.Default));

        if (resolver is null)
            context.ReportDiagnostic(Diagnostic.Create(JsonSerializerContextMissing, null));
    }

    void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var ctxType = context.Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonSerializerContext");
        if (ctxType is null)
            return;

        var serializer = new FindJsonSerializerContext(ctxType).Visit(context.Compilation.GlobalNamespace);

        if (serializer == null)
            context.ReportDiagnostic(Diagnostic.Create(JsonSerializerContextMissing, null));
    }

    void Analyze(SyntaxNodeAnalysisContext context)
    {
        var node = context.Node;
        var info = context.SemanticModel.GetSymbolInfo(node, context.CancellationToken);
        var ctxType = context.SemanticModel.Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonSerializerContext");

        if (ctxType is null ||
            node is not InvocationExpressionSyntax invocation ||
            info.Symbol is not IMethodSymbol symbol ||
            symbol.Name != "AddStreamstoneActorStorage" ||
            symbol.ContainingType?.Name != "StreamstoneSiloBuilderExtensions" ||
            symbol.ContainingType.ContainingAssembly.Name != "Devlooped.CloudActors.Streamstone")
            return;


    }

    class FindJsonSerializerContext(INamedTypeSymbol baseType) : SymbolVisitor<INamedTypeSymbol?>
    {
        public override INamedTypeSymbol? VisitNamespace(INamespaceSymbol symbol)
        {
            foreach (var member in symbol.GetMembers())
            {
                if (member is INamespaceSymbol ns &&
                    VisitNamespace(ns) is { } nsts)
                    return nsts;
                else if (member is INamedTypeSymbol type &&
                    VisitNamedType(type) is { } ts)
                    return ts;
            }
            return default;
        }

        public override INamedTypeSymbol? VisitNamedType(INamedTypeSymbol symbol)
        {
            if (baseType.Equals(symbol.BaseType, SymbolEqualityComparer.Default))
                return symbol;

            return base.VisitNamedType(symbol);
        }
    }
}

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using static Devlooped.CloudActors.Diagnostics;

namespace Devlooped.CloudActors;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class PartialAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(MustBePartial);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(startContext =>
        {
            var actorEvents = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

            startContext.RegisterSyntaxNodeAction(nodeContext =>
            {
                if (nodeContext.Node is InvocationExpressionSyntax invocation)
                {
                    if (nodeContext.SemanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol method &&
                        method.Name == "Raise" &&
                        method.IsGenericMethod &&
                        method.Parameters.Length == 1 &&
                        method.Parameters[0].Type is INamedTypeSymbol eventType)
                    {
                        var enclosingSymbol = nodeContext.SemanticModel.GetEnclosingSymbol(invocation.SpanStart);
                        var containingType = enclosingSymbol?.ContainingType;
                        if (containingType != null && containingType.GetAttributes().Any(a => a.IsActor()))
                        {
                            actorEvents.Add(eventType);
                        }
                    }
                }
            }, SyntaxKind.InvocationExpression);

            startContext.RegisterCompilationEndAction(endContext =>
            {
                var compilation = endContext.Compilation;
                if (compilation.GetTypeByMetadataName("Devlooped.CloudActors.IActorMessage") is not { } messageType)
                    return;

                var messageTypes = compilation
                    .Assembly.GetAllTypes()
                    .OfType<INamedTypeSymbol>()
                    .Where(x => x.AllInterfaces.Contains(messageType, SymbolEqualityComparer.Default));

                // Report also for all source-declared custom types used in the message, as constructors or properties
                var indirect = new HashSet<INamedTypeSymbol>(messageTypes
                    .SelectMany(x => x.GetMembers())
                    .OfType<IPropertySymbol>()
                    // Generated serializers only expose public members.
                    .Where(p => p.DeclaredAccessibility == Accessibility.Public)
                    .Select(p => p.Type)
                    .Concat(messageTypes.SelectMany(x => x.GetMembers()
                        .OfType<IMethodSymbol>()
                        // Generated serializers only expose public members.
                        .Where(m => m.DeclaredAccessibility == Accessibility.Public)
                        .SelectMany(m => m.Parameters)
                        .Select(p => p.Type)))
                    .OfType<INamedTypeSymbol>()
                    // where the type is not partial
                    .Where(t =>
                        !t.GetAttributes().Any(x => x.IsActor()) &&
                        !t.AllInterfaces.Contains(messageType, SymbolEqualityComparer.Default) &&
                        !t.IsPartial() &&
                        t.Locations.Any(l => l.IsInSource)),
                SymbolEqualityComparer.Default);

                foreach (var type in indirect)
                {
                    // select the type declarations
                    if (type.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not TypeDeclarationSyntax declaration)
                        continue;

                    endContext.ReportDiagnostic(Diagnostic.Create(MustBePartial, declaration.Identifier.GetLocation(), type.Name));
                }

                // Report for actor event types
                foreach (var ev in actorEvents.Where(e => !e.IsPartial() && e.Locations.Any(l => l.IsInSource)))
                {
                    if (ev.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is TypeDeclarationSyntax declaration)
                    {
                        endContext.ReportDiagnostic(Diagnostic.Create(MustBePartial, declaration.Identifier.GetLocation(), ev.Name));
                    }
                }
            });
        });

        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ClassDeclaration, SyntaxKind.RecordDeclaration);
    }

    static void Analyze(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not TypeDeclarationSyntax typeDeclaration ||
            context.Compilation.GetTypeByMetadataName("Devlooped.CloudActors.IActorMessage") is not { } messageType)
            return;

        var symbol = context.SemanticModel.GetDeclaredSymbol(typeDeclaration);
        if (symbol is null)
            return;

        // Only require this for actor and message types
        if (!symbol.GetAttributes().Any(x => x.IsActor()) &&
            !symbol.AllInterfaces.Contains(messageType, SymbolEqualityComparer.Default))
            return;

        if (!typeDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            context.ReportDiagnostic(Diagnostic.Create(MustBePartial, typeDeclaration.Identifier.GetLocation(), symbol.Name));
    }
}

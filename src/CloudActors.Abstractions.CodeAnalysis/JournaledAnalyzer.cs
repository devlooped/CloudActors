using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using static Devlooped.CloudActors.Diagnostics;

namespace Devlooped.CloudActors;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
class JournaledAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [JournaledRequiresEventSourced];

#if DEBUG
#pragma warning disable RS1026
#endif
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
#if !DEBUG
        context.EnableConcurrentExecution();
#endif
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ClassDeclaration, SyntaxKind.RecordDeclaration);
    }

    static void Analyze(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not TypeDeclarationSyntax typeDeclaration ||
            context.SemanticModel.GetDeclaredSymbol(typeDeclaration) is not INamedTypeSymbol symbol)
            return;

        if (!symbol.IsJournaled())
            return;

        var isActor = symbol.IsActor();
        var isEventSourced = symbol.AllInterfaces.Any(x => x.ToDisplayString(AnalysisExtensions.FullName) == "Devlooped.CloudActors.IEventSourced");
        if (isActor && isEventSourced)
            return;

        context.ReportDiagnostic(Diagnostic.Create(JournaledRequiresEventSourced, typeDeclaration.Identifier.GetLocation(), symbol.Name));
    }
}

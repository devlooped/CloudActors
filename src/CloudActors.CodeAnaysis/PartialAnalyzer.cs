using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using static Devlooped.CloudActors.Diagnostics;

namespace Devlooped.CloudActors;

[DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
public class PartialAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(MustBePartial);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ClassDeclaration, SyntaxKind.RecordDeclaration);
    }

    void Analyze(SyntaxNodeAnalysisContext context)
    {
        var node = context.Node;
        if (node is not TypeDeclarationSyntax typeDeclaration)
            return;

        if (typeDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            return;

        var symbol = context.SemanticModel.GetDeclaredSymbol(typeDeclaration);
        if (symbol is null)
            return;

        // Only require this for actor command types, denoted by the ActorCommandAttribute.
        if (!symbol.GetAttributes().Any(IsActor))
            return;

        context.ReportDiagnostic(Diagnostic.Create(MustBePartial, typeDeclaration.Identifier.GetLocation()));
    }
}

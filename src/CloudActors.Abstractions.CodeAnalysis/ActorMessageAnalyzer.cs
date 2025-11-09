using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using static Devlooped.CloudActors.Diagnostics;

namespace Devlooped.CloudActors;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ActorMessageAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(SingleInterfaceRequired, MustNotBeSerializable);

#if DEBUG
#pragma warning disable RS1026 // Enable concurrent execution: we only turn this on in release builds
#endif
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
#if !DEBUG
        context.EnableConcurrentExecution();
#endif
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ClassDeclaration, SyntaxKind.RecordDeclaration);
    }

    void Analyze(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not TypeDeclarationSyntax typeDeclaration ||
            context.SemanticModel.GetDeclaredSymbol(typeDeclaration) is not INamedTypeSymbol symbol)
            return;

        if (context.Compilation.GetTypeByMetadataName("Devlooped.CloudActors.IActorCommand") is not INamedTypeSymbol voidCmd ||
            context.Compilation.GetTypeByMetadataName("Devlooped.CloudActors.IActorCommand`1") is not INamedTypeSymbol cmd ||
            context.Compilation.GetTypeByMetadataName("Devlooped.CloudActors.IActorQuery`1") is not INamedTypeSymbol query)
            return;

        var messages = symbol.Interfaces.Where(i =>
            i.Equals(voidCmd, SymbolEqualityComparer.Default) ||
            (i.IsGenericType && i.ConstructedFrom.Equals(cmd, SymbolEqualityComparer.Default)) ||
            (i.IsGenericType && i.ConstructedFrom.Equals(query, SymbolEqualityComparer.Default)));

        if (!messages.Any())
            return;

        if (messages.Skip(1).Any())
        {
            context.ReportDiagnostic(Diagnostic.Create(SingleInterfaceRequired, typeDeclaration.Identifier.GetLocation(), symbol.Name));
        }

        if (context.Compilation.GetTypeByMetadataName("Orleans.GenerateSerializerAttribute") is INamedTypeSymbol generateAttr &&
            typeDeclaration.AttributeLists
                .SelectMany(al => al.Attributes)
                .Select(a => context.SemanticModel.GetSymbolInfo(a).Symbol)
                .Select(s => s is IMethodSymbol ? s.ContainingType : s)
                .Any(a => generateAttr.Equals(a, SymbolEqualityComparer.Default)))
        {
            context.ReportDiagnostic(Diagnostic.Create(MustNotBeSerializable, typeDeclaration.Identifier.GetLocation(), symbol.Name));
        }
    }
}

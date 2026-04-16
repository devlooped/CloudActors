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
class ActorStateSerializableAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(MustBeSerializable);

#if DEBUG
#pragma warning disable RS1026 // Enable concurrent execution: we only turn this on in release builds
#endif
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
#if !DEBUG
        context.EnableConcurrentExecution();
#endif
        context.RegisterCompilationStartAction(startContext =>
        {
            var generateSerializerAttr = startContext.Compilation.GetTypeByMetadataName("Orleans.GenerateSerializerAttribute");

            // Pre-compute the set of types referenced by actor state that need serialization.
            var stateTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

            foreach (var actor in startContext.Compilation.Assembly.GetAllTypes()
                .OfType<INamedTypeSymbol>()
                .Where(t => t.GetAttributes().Any(a => a.IsActor())))
            {
                var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
                var candidates = new List<INamedTypeSymbol>();

                foreach (var prop in actor.GetMembers().OfType<IPropertySymbol>()
                    .Where(x => x.CanBeReferencedByName && !x.IsIndexer && !x.IsAbstract && x.SetMethod != null))
                {
                    AnalysisExtensions.WalkTypeForSerialization(prop.Type, visited, candidates);
                }

                foreach (var field in actor.GetMembers().OfType<IFieldSymbol>()
                    .Where(x => x.CanBeReferencedByName && !x.IsConst && !x.IsStatic && !x.IsReadOnly))
                {
                    AnalysisExtensions.WalkTypeForSerialization(field.Type, visited, candidates);
                }

                foreach (var c in candidates)
                    stateTypes.Add(c);
            }

            // Report locally on each type declaration via syntax node action,
            // so VS shows the diagnostic immediately in the editor.
            startContext.RegisterSyntaxNodeAction(nodeContext =>
            {
                if (nodeContext.Node is not TypeDeclarationSyntax typeDecl)
                    return;

                var type = nodeContext.SemanticModel.GetDeclaredSymbol(typeDecl);
                if (type == null || !stateTypes.Contains(type))
                    return;

                if (type.IsActor() || type.IsActorMessage())
                    return;

                if (type.IsPartial())
                    return;

                if (generateSerializerAttr != null && type.GetAttributes()
                    .Any(a => a.AttributeClass?.Equals(generateSerializerAttr, SymbolEqualityComparer.Default) == true))
                    return;

                nodeContext.ReportDiagnostic(Diagnostic.Create(
                    MustBeSerializable, typeDecl.Identifier.GetLocation(), type.Name));
            }, SyntaxKind.ClassDeclaration, SyntaxKind.RecordDeclaration, SyntaxKind.StructDeclaration, SyntaxKind.RecordStructDeclaration);
        });
    }
}

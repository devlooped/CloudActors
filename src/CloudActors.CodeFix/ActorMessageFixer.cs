using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Devlooped.CloudActors;

[Shared]
[ExportCodeFixProvider(LanguageNames.CSharp)]
public class ActorMessageFixer : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(Diagnostics.MustBeSerializable.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var declaration = root.FindNode(context.Span).FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (declaration == null)
            return;

        context.RegisterCodeFix(
            new AddSerializableAttribute(context.Document, root, declaration),
            context.Diagnostics);
    }

    public class AddSerializableAttribute(Document document, SyntaxNode root, TypeDeclarationSyntax declaration) : CodeAction
    {
        public override string Title => "Add GenerateSerializer attribute";
        public override string EquivalenceKey => Title;

        protected override async Task<Document> GetChangedDocumentAsync(CancellationToken cancellationToken)
        {
            if (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false) is not { } compilation ||
                compilation.GetTypeByMetadataName("Orleans.GenerateSerializerAttribute") is not { } attr ||
                await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false) is not { } semantic)
                return document;

            var list = AttributeList(
                SingletonSeparatedList(
                    Attribute(
                        IdentifierName("GenerateSerializer"))));

            var result = document.WithSyntaxRoot(
                root.ReplaceNode(declaration,
                    declaration.AddAttributeLists(list)));

            var symbols = semantic.LookupSymbols(declaration.GetLocation().SourceSpan.Start, name: "GenerateSerializerAttribute");
            if (symbols.IsDefaultOrEmpty &&
                await result.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) is CompilationUnitSyntax newRoot)
            {
                result = result.WithSyntaxRoot(newRoot
                    .AddUsings(UsingDirective(IdentifierName("Orleans"))));
            }

            return result;
        }
    }
}

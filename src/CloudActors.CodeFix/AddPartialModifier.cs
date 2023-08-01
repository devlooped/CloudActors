using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Devlooped.CloudActors.CodeAnalysis;

public class AddPartialModifier : CodeAction
{
    readonly Document document;
    readonly SyntaxNode root;
    readonly TypeDeclarationSyntax declaration;

    public AddPartialModifier(Document document, SyntaxNode root, TypeDeclarationSyntax declaration)
        => (this.document, this.root, this.declaration)
        = (document, root, declaration);

    public override string Title => "Add partial modifier";
    public override string EquivalenceKey => Title;

    protected override Task<Document> GetChangedDocumentAsync(CancellationToken cancellationToken)
        => Task.FromResult(document.WithSyntaxRoot(
            root.ReplaceNode(declaration,
                declaration.AddModifiers(Token(SyntaxKind.PartialKeyword)))));
}
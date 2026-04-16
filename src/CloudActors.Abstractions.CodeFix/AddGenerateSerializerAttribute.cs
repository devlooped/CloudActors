using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Devlooped.CloudActors.CodeAnalysis;

public class AddGenerateSerializerAttribute(Document document, SyntaxNode root, TypeDeclarationSyntax declaration) : CodeAction
{
    public override string Title => "Add [GenerateSerializer] attribute";
    public override string EquivalenceKey => Title;

    protected override Task<Document> GetChangedDocumentAsync(CancellationToken cancellationToken)
    {
        var attribute = Attribute(ParseName("Orleans.GenerateSerializer"));
        var attributeList = AttributeList(SingletonSeparatedList(attribute));

        var newDeclaration = declaration.AddAttributeLists(attributeList);
        var newRoot = root.ReplaceNode(declaration, newDeclaration);

        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }
}

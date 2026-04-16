using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Devlooped.CloudActors.CodeAnalysis;

[Shared]
[ExportCodeFixProvider(LanguageNames.CSharp)]
public class TypeMustBeSerializable : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Diagnostics.MustBeSerializable.Id];

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
            new AddPartialModifier(context.Document, root, declaration),
            context.Diagnostics);

        context.RegisterCodeFix(
            new AddGenerateSerializerAttribute(context.Document, root, declaration),
            context.Diagnostics);
    }
}

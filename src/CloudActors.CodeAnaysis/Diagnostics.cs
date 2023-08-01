using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Devlooped.CloudActors;

public static class Diagnostics
{
    public static SymbolDisplayFormat FullName { get; } = new(SymbolDisplayGlobalNamespaceStyle.Omitted, SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

    public static bool IsActor(AttributeData attr) => attr.AttributeClass?.ToDisplayString(FullName) == "Devlooped.CloudActors.ActorCommandAttribute";

    public static bool IsPartial(ITypeSymbol node) =>
        node.DeclaringSyntaxReferences.Any(r =>
            r.GetSyntax() is ClassDeclarationSyntax { Modifiers: { } modifiers } &&
            modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword))) ||
        node.DeclaringSyntaxReferences.Any(r =>
        r.GetSyntax() is RecordDeclarationSyntax { Modifiers: { } modifiers } &&
        modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));

    /// <summary>
    /// DCA001: Actor command must be a partial class or record.
    /// </summary>
    public static DiagnosticDescriptor MustBePartial { get; } = new(
        "DCA001",
        "Actor command must be a partial class or record",
        "Cloud Actors require commands to be partial.",
        "Build",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

}

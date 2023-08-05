using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Devlooped.CloudActors;

public static class Diagnostics
{
    public static SymbolDisplayFormat FullName { get; } = new(SymbolDisplayGlobalNamespaceStyle.Omitted, SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

    public static string ToFileName(this ITypeSymbol type) => type.ToDisplayString(FullName).Replace('+', '.');

    public static bool IsActorMessage(ITypeSymbol type) => type.AllInterfaces.Any(x =>
        x.ToDisplayString(FullName).Equals("Devlooped.CloudActors.IActorMessage"));

    public static bool IsActorCommand(this ITypeSymbol type) => type.AllInterfaces.Any(x =>
        x.ToDisplayString(FullName).StartsWith("Devlooped.CloudActors.IActorCommand") && x.IsGenericType);

    public static bool IsActorVoidCommand(this ITypeSymbol type) => type.AllInterfaces.Any(x =>
        x.ToDisplayString(FullName).StartsWith("Devlooped.CloudActors.IActorCommand") && !x.IsGenericType);

    public static bool IsActorQuery(this ITypeSymbol type) => type.AllInterfaces.Any(x =>
        x.ToDisplayString(FullName).StartsWith("Devlooped.CloudActors.IActorQuery") && x.IsGenericType);

    public static bool IsActor(AttributeData attr) =>
        attr.AttributeClass?.ToDisplayString(FullName) == "Devlooped.CloudActors.ActorAttribute";

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
        "Actors must be partial",
        "Cloud Actors require partial actor types.",
        "Build",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

}

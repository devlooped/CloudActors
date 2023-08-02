using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Devlooped.CloudActors;

public static class Diagnostics
{
    public static SymbolDisplayFormat FullName { get; } = new(SymbolDisplayGlobalNamespaceStyle.Omitted, SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

    public static string ToFileName(this ITypeSymbol type) => type.ToDisplayString(FullName).Replace('+', '.');

    public static bool IsActorOperation(ITypeSymbol type) => type.AllInterfaces.Any(x =>
        x.ToDisplayString(FullName).StartsWith("Devlooped.CloudActors.IActorCommand") ||
        x.ToDisplayString(FullName).StartsWith("Devlooped.CloudActors.IActorQuery")) ||
        type.GetAttributes().Any(IsActorOperation);

    public static bool IsActorOperation(AttributeData attr) =>
        IsActorCommand(attr) || IsActorVoidCommand(attr) || IsActorQuery(attr);

    public static bool IsActorCommand(AttributeData attr) =>
        attr.AttributeClass?.ToDisplayString(FullName) == "Devlooped.CloudActors.ActorCommandAttribute" &&
        attr.AttributeClass?.IsGenericType == true;

    public static bool IsActorVoidCommand(AttributeData attr) =>
        attr.AttributeClass?.ToDisplayString(FullName) == "Devlooped.CloudActors.ActorCommandAttribute" &&
        attr.AttributeClass?.IsGenericType == false;

    public static bool IsActorQuery(AttributeData attr) =>
        attr.AttributeClass?.ToDisplayString(FullName) == "Devlooped.CloudActors.ActorQueryAttribute";

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
        "Actor command must be a partial class or record",
        "Cloud Actors require commands to be partial.",
        "Build",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

}

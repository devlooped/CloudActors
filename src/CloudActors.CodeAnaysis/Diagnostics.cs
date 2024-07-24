using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Devlooped.CloudActors;

public static class Diagnostics
{
    /// <summary>
    /// DCA001: Actor must be a partial class or record.
    /// </summary>
    public static DiagnosticDescriptor MustBePartial { get; } = new(
        "DCA001",
        "Actors must be partial",
        "Cloud Actors require partial actor types.",
        "Build",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// DCA002: Actor messages must be serializable.
    /// </summary>
    public static DiagnosticDescriptor MustBeSerializable { get; } = new(
        "DCA002",
        "Actor messages must be serializable",
        "Annotate '{0}' with [GenerateSerializer] attribute as required by Orleans.",
        "Build",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// DCA003: Actor messages can only implement a single message interface.
    /// </summary>
    public static DiagnosticDescriptor SingleInterfaceRequired { get; } = new(
        "DCA003",
        "Actor messages can only implement a single message interface",
        "'{0}' can only implement one of 'IActorCommand', 'IActorCommand<TResult> or 'IActorQuery<TResult>'.",
        "Build",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static SymbolDisplayFormat FullName { get; } = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.ExpandNullable);

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
}

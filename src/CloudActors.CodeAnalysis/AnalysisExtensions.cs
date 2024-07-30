using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Devlooped.CloudActors;

public static class AnalysisExtensions
{
    public static SymbolDisplayFormat FullName { get; } = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.ExpandNullable);

    public static IEnumerable<INamedTypeSymbol> GetAllTypes(this Compilation compilation, bool includeReferenced = true)
        => compilation.Assembly
            .GetAllTypes()
            .OfType<INamedTypeSymbol>()
            .Concat(compilation.GetUsedAssemblyReferences()
            .SelectMany(r =>
            {
                if (compilation.GetAssemblyOrModuleSymbol(r) is IAssemblySymbol asm)
                    return asm.GetAllTypes().OfType<INamedTypeSymbol>();

                return [];
            }));

    public static IEnumerable<INamedTypeSymbol> GetAllTypes(this IAssemblySymbol assembly)
        => GetAllTypes(assembly.GlobalNamespace);

    static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol namespaceSymbol)
    {
        foreach (var typeSymbol in namespaceSymbol.GetTypeMembers())
            yield return typeSymbol;

        foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            foreach (var typeSymbol in GetAllTypes(childNamespace))
            {
                yield return typeSymbol;
            }
        }
    }

    public static string GetTypeName(this ITypeSymbol type, string containingNamespace)
    {
        var typeName = type.ToDisplayString(FullName);
        if (typeName.StartsWith(containingNamespace + "."))
            return typeName.Substring(containingNamespace.Length + 1);

        return typeName;
    }

    public static string ToFileName(this ITypeSymbol type) => type.ToDisplayString(FullName).Replace('+', '.');

    public static bool IsActorMessage(this ITypeSymbol type) => type.AllInterfaces.Any(x =>
        x.ToDisplayString(FullName).Equals("Devlooped.CloudActors.IActorMessage"));

    public static bool IsActorCommand(this ITypeSymbol type) => type.AllInterfaces.Any(x =>
        x.ToDisplayString(FullName).StartsWith("Devlooped.CloudActors.IActorCommand") && x.IsGenericType);

    public static bool IsActorVoidCommand(this ITypeSymbol type) => type.AllInterfaces.Any(x =>
        x.ToDisplayString(FullName).StartsWith("Devlooped.CloudActors.IActorCommand") && !x.IsGenericType);

    public static bool IsActorQuery(this ITypeSymbol type) => type.AllInterfaces.Any(x =>
        x.ToDisplayString(FullName).StartsWith("Devlooped.CloudActors.IActorQuery") && x.IsGenericType);

    public static bool IsActor(this ITypeSymbol type) => type.GetAttributes().Any(a => a.IsActor());

    public static bool IsActor(this AttributeData attr) =>
        attr.AttributeClass?.ToDisplayString(FullName) == "Devlooped.CloudActors.ActorAttribute";

    public static bool IsPartial(this ITypeSymbol node) => node.DeclaringSyntaxReferences.Any(
        r => r.GetSyntax() is TypeDeclarationSyntax { Modifiers: { } modifiers } &&
            modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));
}

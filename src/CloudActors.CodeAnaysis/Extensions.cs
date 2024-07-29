using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Devlooped.CloudActors;

static class Extensions
{
    public static IEnumerable<ITypeSymbol> GetAllTypes(this IAssemblySymbol assembly)
        => GetAllTypes(assembly.GlobalNamespace);

    static IEnumerable<ITypeSymbol> GetAllTypes(INamespaceSymbol namespaceSymbol)
    {
        foreach (var typeSymbol in namespaceSymbol.GetTypeMembers())
        {
            yield return typeSymbol;
        }

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
        var typeName = type.ToDisplayString(Diagnostics.FullName);
        if (typeName.StartsWith(containingNamespace + "."))
            return typeName.Substring(containingNamespace.Length + 1);

        return typeName;
    }

    public static bool IsPartial(this INamedTypeSymbol type)
    {
        foreach (var syntax in type.DeclaringSyntaxReferences)
        {
            if (syntax.GetSyntax() is TypeDeclarationSyntax typeDeclarationSyntax &&
                typeDeclarationSyntax.Modifiers.Any(SyntaxKind.PartialKeyword))
                return true;
        }

        return false;
    }
}

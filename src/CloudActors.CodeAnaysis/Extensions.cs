using System.Collections.Generic;
using Microsoft.CodeAnalysis;

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
}

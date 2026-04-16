using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Devlooped.CloudActors;

static class AnalysisExtensions
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

    /// <summary>
    /// Returns true if the type is a user-defined type that may need Orleans serialization:
    /// declared in source, not a primitive/enum/delegate/interface, not in System.* or Orleans.* namespaces.
    /// </summary>
    public static bool IsUserDefinedSerializableCandidate(this INamedTypeSymbol type)
    {
        if (!type.Locations.Any(l => l.IsInSource))
            return false;
        if (type.SpecialType != SpecialType.None)
            return false;
        if (type.TypeKind is TypeKind.Enum or TypeKind.Delegate or TypeKind.Interface)
            return false;

        var ns = type.ContainingNamespace?.ToDisplayString();
        if (ns != null && (ns.StartsWith("System") || ns.StartsWith("Orleans")))
            return false;

        return true;
    }

    /// <summary>
    /// Recursively walks a type tree collecting user-defined types that need serialization.
    /// Handles arrays, generic type arguments, and nested public properties.
    /// </summary>
    public static void WalkTypeForSerialization(
        ITypeSymbol type,
        HashSet<ITypeSymbol> visited,
        List<INamedTypeSymbol> results)
    {
        if (!visited.Add(type))
            return;

        // Handle arrays
        if (type is IArrayTypeSymbol arr)
        {
            WalkTypeForSerialization(arr.ElementType, visited, results);
            return;
        }

        if (type is not INamedTypeSymbol named)
            return;

        // Walk generic type arguments (List<T>, Dictionary<K,V>, Nullable<T>, etc.)
        foreach (var arg in named.TypeArguments)
            WalkTypeForSerialization(arg, visited, results);

        if (!named.IsUserDefinedSerializableCandidate())
            return;

        // This is a user-defined type that needs serialization
        results.Add(named);

        // Recursively walk its public properties to find nested types
        foreach (var prop in named.GetMembers().OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public))
        {
            WalkTypeForSerialization(prop.Type, visited, results);
        }

        // Also walk public fields (e.g. record positional parameters)
        foreach (var field in named.GetMembers().OfType<IFieldSymbol>()
            .Where(f => f.DeclaredAccessibility == Accessibility.Public && !f.IsConst && !f.IsStatic))
        {
            WalkTypeForSerialization(field.Type, visited, results);
        }
    }

    /// <summary>
    /// Collects partial user-defined types from actor state members as <see cref="SerializableTypeModel"/> values.
    /// Used by the generator pipeline to emit [GenerateSerializer] for state-referenced types.
    /// Skips types already handled by ActorMessageGenerator (message types and their additional types).
    /// </summary>
    public static ImmutableArray<SerializableTypeModel> CollectStateSerializableTypes(
        INamedTypeSymbol actor, string actorNamespace, Compilation compilation)
    {
        var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var candidates = new List<INamedTypeSymbol>();

        // Walk writable properties (same criteria as ActorStateGenerator)
        foreach (var prop in actor.GetMembers().OfType<IPropertySymbol>()
            .Where(x => x.CanBeReferencedByName && !x.IsIndexer && !x.IsAbstract && x.SetMethod != null))
        {
            WalkTypeForSerialization(prop.Type, visited, candidates);
        }

        // Walk mutable fields
        foreach (var field in actor.GetMembers().OfType<IFieldSymbol>()
            .Where(x => x.CanBeReferencedByName && !x.IsConst && !x.IsStatic && !x.IsReadOnly))
        {
            WalkTypeForSerialization(field.Type, visited, candidates);
        }

        // Collect types already handled by ActorMessageGenerator (message additional types)
        var messageAdditionalTypes = CollectMessageAdditionalTypes(compilation);

        // Only return partial, non-actor, non-message types not already covered by message generation
        return candidates
            .Where(t => t.IsPartial() && !t.IsActorMessage() && !t.IsActor() &&
                !messageAdditionalTypes.Contains(t))
            .Select(t => new SerializableTypeModel(
                t.Name,
                t.ContainingNamespace.IsGlobalNamespace ? "" : t.ContainingNamespace.ToDisplayString(),
                t.ToDisplayString(FullName),
                t.IsRecord))
            .Distinct()
            .ToImmutableArray();
    }

    /// <summary>
    /// Collects all types that ActorMessageGenerator will generate [GenerateSerializer] for,
    /// to avoid duplicate generation from ActorStateGenerator.
    /// </summary>
    static HashSet<INamedTypeSymbol> CollectMessageAdditionalTypes(Compilation compilation)
    {
        var messageType = compilation.GetTypeByMetadataName("Devlooped.CloudActors.IActorMessage");
        var result = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        if (messageType == null)
            return result;

        // Find all message types in the current assembly
        foreach (var type in compilation.Assembly.GetAllTypes().OfType<INamedTypeSymbol>())
        {
            if (!type.AllInterfaces.Contains(messageType, SymbolEqualityComparer.Default) || !type.IsPartial())
                continue;

            // The message itself is handled
            result.Add(type);

            // Collect additional types from public properties/parameters (same logic as ExtractActorMessageModel)
            foreach (var prop in type.GetMembers().OfType<IPropertySymbol>()
                .Where(p => p.DeclaredAccessibility == Accessibility.Public))
            {
                if (prop.Type is INamedTypeSymbol propType && propType.IsPartial() && !propType.IsActorMessage())
                    result.Add(propType);
            }

            foreach (var method in type.GetMembers().OfType<IMethodSymbol>()
                .Where(m => m.DeclaredAccessibility == Accessibility.Public))
            {
                foreach (var param in method.Parameters)
                {
                    if (param.Type is INamedTypeSymbol paramType && paramType.IsPartial() && !paramType.IsActorMessage())
                        result.Add(paramType);
                }
            }
        }

        return result;
    }
}

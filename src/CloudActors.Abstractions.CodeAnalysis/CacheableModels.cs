using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Devlooped.CloudActors.AnalysisExtensions;

namespace Devlooped.CloudActors;

// ──────────────────────────────────────────────
// Actor discovery model (shared across generators)
// ──────────────────────────────────────────────

/// <summary>
/// String-only model representing a discovered actor type.
/// Used in pipeline values for proper incremental caching.
/// </summary>
record struct ActorModel(
    string Name,
    string Namespace,
    string FullName,
    string? StateName,
    string? StorageProvider,
    bool IsEventSourced,
    bool IsPartial,
    EquatableArray<ActorMemberModel> Properties,
    EquatableArray<ActorMemberModel> Fields,
    EquatableArray<GrainOperationModel> Queries,
    EquatableArray<GrainOperationModel> Commands,
    EquatableArray<GrainOperationModel> VoidCommands,
    /// <summary>Display string of the first ctor parameter type, or null if no ctor/string id.</summary>
    string? IdTypeFullName,
    /// <summary>Whether the ID type is a primitive/BCL value type.</summary>
    bool IsPrimitiveId,
    /// <summary>Whether the ID type implements IParsable or is a StructId.</summary>
    bool IsTypedId,
    /// <summary>Whether the Guid type has CreateVersion7 method (for primitive Guid IDs).</summary>
    bool HasGuidCreateVersion7,
    /// <summary>Partial user-defined types referenced from actor state that need [GenerateSerializer].</summary>
    EquatableArray<SerializableTypeModel> StateTypes,
    /// <summary>Event type names for event-sourced actors (for [JsonSerializable] generation).</summary>
    EquatableArray<string> EventTypes) : IEquatable<ActorModel>
{
    public string FileName => FullName.Replace('+', '.');
}

record struct ActorMemberModel(string Name, string TypeName) : IEquatable<ActorMemberModel>
{
    public readonly string Type => TypeName switch
    {
        "System.String" => "string",
        "System.Int32" => "int",
        "System.Int64" => "long",
        "System.Boolean" => "bool",
        "System.Single" => "float",
        "System.Double" => "double",
        "System.Decimal" => "decimal",
        "System.DateTime" => "DateTime",
        "System.Guid" => "Guid",
        "System.TimeSpan" => "TimeSpan",
        "System.Byte" => "byte",
        "System.Byte[]" => "byte[]",
        "System.Char" => "char",
        "System.UInt32" => "uint",
        "System.UInt64" => "ulong",
        "System.SByte" => "sbyte",
        "System.UInt16" => "ushort",
        "System.Int16" => "short",
        "System.Object" => "object",
        _ => TypeName
    };
}

record struct GrainOperationModel(string Name, string Type, bool IsAsync, string? ReturnTypeFullName = null) : IEquatable<GrainOperationModel>;

// ──────────────────────────────────────────────
// Actor message model
// ──────────────────────────────────────────────

enum ActorMessageKind
{
    VoidCommand,
    Command,
    Query
}

/// <summary>
/// Minimal model for a type that needs [GenerateSerializer] annotation.
/// </summary>
record struct SerializableTypeModel(
    string Name,
    string Namespace,
    string FullName,
    bool IsRecord) : IEquatable<SerializableTypeModel>
{
    public readonly string FileName => FullName.Replace('+', '.');
}

/// <summary>
/// String-only model for an actor message type.
/// </summary>
record struct ActorMessageModel(
    string Name,
    string Namespace,
    string FullName,
    bool IsRecord,
    bool IsPartial,
    ActorMessageKind Kind,
    /// <summary>For Command&lt;T&gt; or Query&lt;T&gt;, the return type. Null for VoidCommand.</summary>
    string? ReturnTypeFullName,
    EquatableArray<SerializableTypeModel> AdditionalTypes) : IEquatable<ActorMessageModel>
{
    public readonly string FileName => FullName.Replace('+', '.');
}

// ──────────────────────────────────────────────
// Event-sourced model
// ──────────────────────────────────────────────

record struct EventSourcedModel(
    string Namespace,
    string Name,
    string FullName,
    EquatableArray<string> Events,
    string Version = ThisAssembly.Info.InformationalVersion) : IEquatable<EventSourcedModel>;

// ──────────────────────────────────────────────
// Orleans config (string-only, no Compilation)
// ──────────────────────────────────────────────

record struct OrleansConfig(
    bool IsCloudActorsServer,
    bool ProduceReferenceAssembly,
    string? GenerateFieldIds,
    bool GenerateCompatibilityInvokers) : IEquatable<OrleansConfig>;

// ──────────────────────────────────────────────
// Assembly reference model
// ──────────────────────────────────────────────

record struct CloudActorAssemblyModel(
    string AssemblyName,
    /// <summary>A type from the assembly that can be used in typeof() for GenerateCodeForDeclaringAssembly.</summary>
    string? AccessibleTypeName) : IEquatable<CloudActorAssemblyModel>;

// ──────────────────────────────────────────────
// Model extractors
// ──────────────────────────────────────────────

static class ModelExtractors
{
    /// <summary>
    /// Extracts an <see cref="ActorModel"/> from a declared actor symbol.
    /// Must be called inside a pipeline transform lambda where semantic model is available.
    /// </summary>
    public static ActorModel? ExtractActorModel(INamedTypeSymbol actor, INamedTypeSymbol? iParsable, bool hasGuidCreateVersion7, Compilation? compilation = null)
    {
        if (actor.ContainingType != null)
            return null;

        var attribute = actor.GetAttributes().FirstOrDefault(x => x.IsActor());
        if (attribute == null)
            return null;

        var state = default(string);
        var storage = default(string);
        if (attribute.ConstructorArguments.Length >= 1 && !attribute.ConstructorArguments[0].IsNull)
            state = attribute.ConstructorArguments[0].Value?.ToString();
        if (attribute.ConstructorArguments.Length == 2 && !attribute.ConstructorArguments[1].IsNull)
            storage = attribute.ConstructorArguments[1].Value?.ToString();

        var ns = actor.ContainingNamespace.IsGlobalNamespace ? "" : actor.ContainingNamespace.ToDisplayString();
        var isEventSourced = actor.AllInterfaces.Any(x => x.ToDisplayString(FullName) == "Devlooped.CloudActors.IEventSourced");

        // Extract members for state
        var props = actor.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(x => x.CanBeReferencedByName && !x.IsIndexer && !x.IsAbstract && x.SetMethod != null)
            .Select(x => new ActorMemberModel(x.Name, x.Type.GetTypeName(ns)))
            .ToImmutableArray();

        var fields = actor.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(x => x.CanBeReferencedByName && !x.IsConst && !x.IsStatic && !x.IsReadOnly)
            .Select(x => new ActorMemberModel(x.Name, x.Type.GetTypeName(ns)))
            .ToImmutableArray();

        // Extract grain operations
        var queries = actor.GetMembers().OfType<IMethodSymbol>()
            .Where(m => m.Parameters.Length == 1 && m.Parameters[0].Type.IsActorQuery())
            .Select(m =>
            {
                var paramType = m.Parameters[0].Type;
                var queryIface = paramType.AllInterfaces.FirstOrDefault(i =>
                    i.IsGenericType &&
                    i.ContainingNamespace?.ToDisplayString() == "Devlooped.CloudActors" &&
                    i.Name == "IActorQuery");
                return new GrainOperationModel(
                    m.Name,
                    paramType.ToDisplayString(FullName),
                    m.ReturnType.ToDisplayString(FullName).StartsWith("System.Threading.Tasks.Task"),
                    queryIface?.TypeArguments[0].ToDisplayString(FullName));
            })
            .ToImmutableArray();

        var commands = actor.GetMembers().OfType<IMethodSymbol>()
            .Where(m => m.Parameters.Length == 1 && m.Parameters[0].Type.IsActorCommand())
            .Select(m =>
            {
                var paramType = m.Parameters[0].Type;
                var cmdIface = paramType.AllInterfaces.FirstOrDefault(i =>
                    i.IsGenericType &&
                    i.ContainingNamespace?.ToDisplayString() == "Devlooped.CloudActors" &&
                    i.Name == "IActorCommand");
                return new GrainOperationModel(
                    m.Name,
                    paramType.ToDisplayString(FullName),
                    m.ReturnType.ToDisplayString(FullName).StartsWith("System.Threading.Tasks.Task"),
                    cmdIface?.TypeArguments[0].ToDisplayString(FullName));
            })
            .ToImmutableArray();

        // Exclude IActorCommand<T> types: they implement IActorCommand too (non-generic), but belong in commands only.
        var voidCommands = actor.GetMembers().OfType<IMethodSymbol>()
            .Where(m => m.Parameters.Length == 1
                && m.Parameters[0].Type.IsActorVoidCommand()
                && !m.Parameters[0].Type.IsActorCommand())
            .Select(m => new GrainOperationModel(
                m.Name,
                m.Parameters[0].Type.ToDisplayString(FullName),
                m.ReturnType.ToDisplayString(FullName).StartsWith("System.Threading.Tasks.Task")))
            .ToImmutableArray();

        // Extract ID info
        var (idTypeFullName, isPrimitiveId, isTypedId) = ExtractIdInfo(actor, iParsable);

        // Collect partial user-defined types from state members that need [GenerateSerializer]
        var stateTypes = compilation != null
            ? AnalysisExtensions.CollectStateSerializableTypes(actor, ns, compilation)
            : ImmutableArray<SerializableTypeModel>.Empty;

        // Collect event types from partial void Apply(EventType) methods
        var eventTypes = isEventSourced
            ? [.. actor.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m.Name == "Apply" && m.ReturnsVoid && m.Parameters.Length == 1 && !m.IsAbstract)
                .Select(m => m.Parameters[0].Type.ToDisplayString(FullName))
                .Distinct()]
            : ImmutableArray<string>.Empty;

        return new ActorModel(
            Name: actor.Name,
            Namespace: ns,
            FullName: actor.ToDisplayString(FullName),
            StateName: state,
            StorageProvider: storage,
            IsEventSourced: isEventSourced,
            IsPartial: actor.IsPartial(),
            Properties: props,
            Fields: fields,
            Queries: queries,
            Commands: commands,
            VoidCommands: voidCommands,
            IdTypeFullName: idTypeFullName,
            IsPrimitiveId: isPrimitiveId,
            IsTypedId: isTypedId,
            HasGuidCreateVersion7: hasGuidCreateVersion7,
            StateTypes: stateTypes,
            EventTypes: eventTypes);
    }

    static (string? IdTypeFullName, bool IsPrimitive, bool IsTypedId) ExtractIdInfo(
        INamedTypeSymbol actor, INamedTypeSymbol? iParsable)
    {
        var ctor = actor.Constructors
            .Where(c => !c.IsStatic && c.Parameters.Length > 0)
            .OrderBy(c => c.Parameters.Length)
            .FirstOrDefault();

        if (ctor is null)
            return (null, false, false);

        var idType = ctor.Parameters[0].Type;

        var idTypeName = idType.ToDisplayString(FullName);
        var isPrimitive = IsPrimitiveType(idType);

        if (isPrimitive)
            return (idTypeName, true, false);

        // Check IParsable
        if (iParsable is not null)
        {
            var implementsParsable = idType.AllInterfaces.Any(i =>
                i.IsGenericType &&
                i.ConstructedFrom.Equals(iParsable, SymbolEqualityComparer.Default));

            if (implementsParsable)
                return (idTypeName, false, true);
        }

        // Check StructId
        var isStructId = idType.AllInterfaces.Any(i =>
            i.ContainingNamespace?.Name == "StructId" && i.Name == "IStructId");

        if (isStructId)
            return (idTypeName, false, true);

        return (idTypeName, false, false);
    }

    /// <summary>
    /// A type is "primitive" if it's a well-known system value type:
    /// has a non-None SpecialType, or is a value type in the System namespace.
    /// </summary>
    internal static bool IsPrimitiveType(ITypeSymbol type)
    {
        if (type.SpecialType != SpecialType.None)
            return true;

        if (type.IsValueType && type.ContainingNamespace?.ToDisplayString() == "System")
            return true;

        return false;
    }

    /// <summary>
    /// Extracts an <see cref="ActorMessageModel"/> from a declared message symbol.
    /// </summary>
    public static ActorMessageModel? ExtractActorMessageModel(
        INamedTypeSymbol type,
        INamedTypeSymbol? voidCommand,
        INamedTypeSymbol? command,
        INamedTypeSymbol? query)
    {
        if (!type.IsActorMessage())
            return null;

        var kind = ActorMessageKind.VoidCommand;
        string? returnType = null;

        if (voidCommand != null && type.AllInterfaces.Contains(voidCommand, SymbolEqualityComparer.Default))
        {
            kind = ActorMessageKind.VoidCommand;
        }
        else if (command != null &&
            type.AllInterfaces.FirstOrDefault(x => x.IsGenericType && x.ConstructedFrom.Equals(command, SymbolEqualityComparer.Default)) is INamedTypeSymbol cmdIface)
        {
            kind = ActorMessageKind.Command;
            returnType = cmdIface.TypeArguments[0].ToDisplayString(FullName);
        }
        else if (query != null &&
            type.AllInterfaces.FirstOrDefault(x => x.IsGenericType && x.ConstructedFrom.Equals(query, SymbolEqualityComparer.Default)) is INamedTypeSymbol queryIface)
        {
            kind = ActorMessageKind.Query;
            returnType = queryIface.TypeArguments[0].ToDisplayString(FullName);
        }

        var ns = type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString();

        // Collect additional types from public members that need serialization
        var additionalTypes = type.GetMembers().OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public)
            .Select(p => p.Type)
            .OfType<INamedTypeSymbol>()
            .Where(t => t.IsPartial() && !t.IsActorMessage())
            .Concat(type.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m.DeclaredAccessibility == Accessibility.Public)
                .SelectMany(m => m.Parameters)
                .Select(p => p.Type)
                .OfType<INamedTypeSymbol>()
                .Where(t => !t.IsActorMessage() && t.IsPartial()))
            .Select(t => new SerializableTypeModel(
                t.Name,
                t.ContainingNamespace.IsGlobalNamespace ? "" : t.ContainingNamespace.ToDisplayString(),
                t.ToDisplayString(FullName),
                t.IsRecord))
            .Distinct()
            .ToImmutableArray();

        return new ActorMessageModel(
            Name: type.Name,
            Namespace: ns,
            FullName: type.ToDisplayString(FullName),
            IsRecord: type.IsRecord,
            IsPartial: type.IsPartial(),
            Kind: kind,
            ReturnTypeFullName: returnType,
            AdditionalTypes: additionalTypes);
    }

    /// <summary>
    /// Extracts <see cref="OrleansConfig"/> from analyzer config options.
    /// </summary>
    public static OrleansConfig ExtractOrleansConfig(Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions options)
    {
        var isServer = options.TryGetValue("build_property.IsCloudActorsServer", out var sv) &&
            bool.TryParse(sv, out var isS) && isS;
        var produceRef = options.TryGetValue("build_property.ProduceReferenceAssembly", out var pv) &&
            bool.TryParse(pv, out var pr) && pr;

        options.TryGetValue("build_property.orleans_generatefieldids", out var genFieldIds);

        var genCompatInvokers = false;
        if (options.TryGetValue("build_property.orleansgeneratecompatibilityinvokers", out var gci) &&
            bool.TryParse(gci, out var gciBool))
            genCompatInvokers = gciBool;

        return new OrleansConfig(isServer, produceRef, genFieldIds, genCompatInvokers);
    }

    /// <summary>
    /// Discovers event type names raised by an actor by walking Raise&lt;T&gt;() 
    /// invocations syntactically in the actor's source code.
    /// </summary>
    public static ImmutableArray<string> ExtractRaisedEventNames(INamedTypeSymbol actor, Compilation compilation)
    {
        var events = new System.Collections.Generic.HashSet<string>();
        foreach (var syntaxRef in actor.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            var tree = syntax.SyntaxTree;
            var semantic = compilation.GetSemanticModel(tree);

            foreach (var invocation in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (semantic.GetSymbolInfo(invocation).Symbol is IMethodSymbol method &&
                    method.Name == "Raise" &&
                    method.IsGenericMethod &&
                    method.TypeArguments.Length == 1 &&
                    method.TypeArguments[0] is INamedTypeSymbol eventType)
                {
                    var ns = actor.ContainingNamespace.ToDisplayString();
                    events.Add(eventType.GetTypeName(ns));
                }
            }
        }
        return [.. events];
    }
}

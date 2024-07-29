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
        "Actors and messages must be partial",
        "Add the partial keyword to '{0}' as required for types used by actors.",
        "Build",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// DCA002: Actor messages must be serializable.
    /// </summary>
    public static DiagnosticDescriptor MustNotBeSerializable { get; } = new(
        "DCA002",
        "Actor messages must not be annotated as serializable",
        "Do not annotate '{0}' with [GenerateSerializer] as it's done automatically.",
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

    /// <summary>
    /// DCA004: Indirectly used types must be serializable
    /// </summary>
    public static DiagnosticDescriptor MustBeSerializable { get; } = new(
        "DCA004",
        "Types used in actor messages must have a [GenerateSerializer] attribute,",
        "Annotate '{0}' with [GenerateSerializer] as it is used by at least one actor message.",
        "Build",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}

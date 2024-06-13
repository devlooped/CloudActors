using Microsoft.CodeAnalysis;

namespace Devlooped.CloudActors;

public static class Diagnostics
{
    /// <summary>
    /// DCAS001: Provide optimized type information for actor types.
    /// </summary>
    public static DiagnosticDescriptor JsonSerializerContextMissing { get; } = new(
        "DCAS001",
        "Provide optimized serialization information for actor types",
        "JsonSerializerContext-derived class not found in the current assembly",
        "Build",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}

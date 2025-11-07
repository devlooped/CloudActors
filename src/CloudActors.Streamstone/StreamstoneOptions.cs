using System.Text.Json;
using System.Text.Json.Serialization;

namespace Devlooped.CloudActors;

/// <summary>Provides options for configuring <see cref="StreamstoneStorage"/> behavior.</summary>
public class StreamstoneOptions
{
    static readonly JsonSerializerOptions options = new()
    {
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        IncludeFields = true,
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Default options to use when creating a <see cref="StreamstoneStorage"/> instance.</summary>
    public static StreamstoneOptions Default { get; } = new();

    /// <summary>When true, will automatically create a snapshot of the state for easy retrieval.</summary>
    public bool AutoSnapshot { get; set; } = true;

    /// <summary>The settings to use when serializing and deserializing events, and snapshot if <see cref="AutoSnapshot"/> is true.</summary>
    public JsonSerializerOptions JsonOptions { get; set; } = options;
}

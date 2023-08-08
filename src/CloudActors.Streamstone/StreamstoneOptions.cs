using System.Text.Json;
using System.Text.Json.Serialization;

namespace Devlooped.CloudActors;

public class StreamstoneOptions
{
    static readonly JsonSerializerOptions options = new()
    {
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Default options to use when creating a <see cref="StreamstoneStorage"/> instance.
    /// </summary>
    public static StreamstoneOptions Default { get; } = new();

    /// <summary>
    /// When true, will automatically create a snapshot of the state every <see cref="SnapshotThreshold"/> events.
    /// In order to include properties with private setters in the snapshot, the type must be annotated with 
    /// [JsonInclude].
    /// </summary>
    public bool AutoSnapshot { get; set; } = true;

    /// <summary>
    /// The settings to use when serializing and deserializing events and snapshot 
    /// if <see cref="AutoSnapshot"/> is true.
    /// </summary>
    public JsonSerializerOptions JsonOptions { get; set; } = options;
}

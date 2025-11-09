using System.Text.Json;
using System.Text.Json.Serialization;

namespace Devlooped.CloudActors;

/// <summary>Specifies the level of version compatibility to use with snpashots.</summary>
public enum SnapshotVersionCompatibility
{
    /// <summary>Consider serialized state snapshot with the same major version as compatible.</summary>
    Major,
    /// <summary>Consider serialized state snapshot with the same major and minor version as compatible.</summary>
    Minor,
}

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

    /// <summary>The snapshot version compatibility to use when deserializing state.</summary>
    /// <remarks>
    /// When <see cref="AutoSnapshot"/> is true, snapshots will be created automatically on save, 
    /// and when loading state, the most recent snapshot will be used as a starting point to avoid 
    /// reading and replaying all events from the stream. This can greatly improve performance.
    /// <para>
    /// This property controls how version compatibility is determined when deserializing snapshots.
    /// By default, snapshots are considered compatible if the serialized state major and minor 
    /// version match with the current actor state assembly version.
    /// </para>
    /// </remarks>
    public SnapshotVersionCompatibility SnapshotCompatibility { get; set; } = SnapshotVersionCompatibility.Minor;

    /// <summary>The settings to use when serializing and deserializing events, and snapshot if <see cref="AutoSnapshot"/> is true.</summary>
    public JsonSerializerOptions JsonOptions { get; set; } = options;
}

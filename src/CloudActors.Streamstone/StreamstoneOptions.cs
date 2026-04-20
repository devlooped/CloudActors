using System;
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

    /// <summary>
    /// When <see langword="true"/>, writes from event-sourced actors are queued and persisted in the background, returning
    /// from <see cref="StreamstoneStorage.WriteStateAsync"/> as soon as the events have been accepted by the actor instead
    /// of waiting for the underlying Azure Table Storage round-trip to complete. Bursts of writes are coalesced into batched
    /// <c>Stream.WriteAsync</c> calls. The queue is flushed on grain deactivation. If a write ultimately fails after the
    /// configured retries, the grain is forcefully deactivated so it reloads from storage on next activation.
    /// </summary>
    /// <remarks>
    /// Only applies to actors implementing <see cref="IEventSourced"/>. Snapshot-only actors continue to write synchronously
    /// regardless of this flag. Defaults to <see langword="false"/>, preserving the synchronous write semantics.
    /// </remarks>
    public bool BackgroundSave { get; set; }

    /// <summary>
    /// Maximum number of attempts the background writer will perform before giving up and forcing the grain to deactivate.
    /// Only relevant when <see cref="BackgroundSave"/> is <see langword="true"/>. Defaults to <c>5</c>.
    /// </summary>
    public int BackgroundSaveMaxAttempts { get; set; } = 5;

    /// <summary>
    /// Initial backoff delay between retry attempts in the background writer. Each subsequent retry doubles this delay (capped at 30s).
    /// Only relevant when <see cref="BackgroundSave"/> is <see langword="true"/>. Defaults to <c>200ms</c>.
    /// </summary>
    public TimeSpan BackgroundSaveRetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);
}

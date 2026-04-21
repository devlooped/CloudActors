using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Devlooped;
using Orleans.Runtime;
using Streamstone;
using StreamEventId = Streamstone.EventId;

namespace Devlooped.CloudActors;

[EditorBrowsable(EditorBrowsableState.Never)]
/// <summary>Provides shared Streamstone storage primitives for journaled actor state and event persistence.</summary>
public static class StreamstoneJournaledStorage
{
    static readonly Version UnknownVersion = new(0, 0);
    const string UnknownVersionString = "0.0";

    /// <summary>Reads the current journaled actor view from Streamstone, replaying events when needed.</summary>
    public static Task<KeyValuePair<int, TState>> ReadStateAsync<TState>(
        CloudStorageAccount storage,
        StreamstoneOptions options,
        string stateName,
        GrainId grainId,
        JsonSerializerOptions jsonOptions,
        Func<TState> createState,
        Action<TState, IReadOnlyList<object>> applyEvents)
        => ReadStateAsync(
            name => GetTable(storage, name),
            options,
            stateName,
            grainId,
            jsonOptions,
            createState,
            applyEvents);

    internal static async Task<KeyValuePair<int, TState>> ReadStateAsync<TState>(
        Func<string, Task<TableClient>> getTable,
        StreamstoneOptions options,
        string stateName,
        GrainId grainId,
        JsonSerializerOptions jsonOptions,
        Func<TState> createState,
        Action<TState, IReadOnlyList<object>> applyEvents)
    {
        var table = await getTable(stateName);
        var rowId = grainId.Key.ToString();
        var partition = new Partition(table, rowId);
        var stream = await Stream.TryOpenAsync(partition);
        if (!stream.Found)
            return new(0, createState());

        if (options.AutoSnapshot)
        {
            var result = await table.GetEntityIfExistsAsync<EventEntity>(rowId, "State");
            if (result.HasValue && result.Value is EventEntity entity &&
                typeof(TState).Assembly.GetName() is { } asm &&
                IsCompatible(options, asm.Version ?? UnknownVersion, entity.DataVersion) &&
                entity.Data is string data &&
                JsonSerializer.Deserialize<TState>(data, jsonOptions) is { } instance)
            {
                return new(entity.Version ?? stream.Stream.Version, instance);
            }
        }

        var entities = await Stream.ReadAsync<EventEntity>(partition);
        var state = createState();
        if (entities?.HasEvents == true)
            applyEvents(state, entities.Events.Select(e => ToDomainEvent(e, jsonOptions)).ToArray());

        return new(stream.Stream.Version, state);
    }

    /// <summary>Appends journaled events to Streamstone and optionally writes an updated snapshot.</summary>
    public static Task<int?> AppendEventsAsync<TState>(
        CloudStorageAccount storage,
        StreamstoneOptions options,
        string stateName,
        GrainId grainId,
        JsonSerializerOptions jsonOptions,
        int expectedVersion,
        IReadOnlyList<object> updates,
        TState snapshot)
        => AppendEventsAsync(
            name => GetTable(storage, name),
            options,
            stateName,
            grainId,
            jsonOptions,
            expectedVersion,
            updates,
            snapshot);

    internal static async Task<int?> AppendEventsAsync<TState>(
        Func<string, Task<TableClient>> getTable,
        StreamstoneOptions options,
        string stateName,
        GrainId grainId,
        JsonSerializerOptions jsonOptions,
        int expectedVersion,
        IReadOnlyList<object> updates,
        TState snapshot)
    {
        if (updates.Count == 0)
            return expectedVersion;

        var table = await getTable(stateName);
        var partition = new Partition(table, grainId.Key.ToString());
        var asm = typeof(TState).Assembly.GetName();

        try
        {
            var newVersion = expectedVersion + updates.Count;
            var includes = options.AutoSnapshot ?
                [
                    new EventEntity
                    {
                        PartitionKey = table.Name,
                        RowKey = "State",
                        Data = JsonSerializer.Serialize(snapshot, jsonOptions),
                        DataVersion = asm.Version?.ToString(2) ?? UnknownVersionString,
                        Type = $"{typeof(TState).FullName ?? typeof(TState).Name}, {asm.Name}",
                        Version = newVersion
                    }
                ] : Array.Empty<ITableEntity>();

            await Stream.WriteAsync(partition,
                expectedVersion,
                [.. updates.Select((e, i) => ToEventData(e, expectedVersion + i, jsonOptions, includes))]);

            return newVersion;
        }
        catch (ConcurrencyConflictException)
        {
            return null;
        }
    }

    static async Task<TableClient> GetTable(CloudStorageAccount storage, string name)
    {
        var client = storage.CreateCloudTableClient();
        var table = client.GetTableClient(name);
        await table.CreateIfNotExistsAsync();
        return table;
    }

    static bool IsCompatible(StreamstoneOptions options, Version assemblyVersion, string? dataVersion)
    {
        if (dataVersion == null || !Version.TryParse(dataVersion, out var version))
            return false;

        return options.SnapshotCompatibility switch
        {
            SnapshotVersionCompatibility.Major => assemblyVersion.Major == version.Major,
            SnapshotVersionCompatibility.Minor => assemblyVersion.Major == version.Major && assemblyVersion.Minor == version.Minor,
            _ => false,
        };
    }

    static object ToDomainEvent(EventEntity entity, JsonSerializerOptions jsonOptions)
    {
        if (entity.Data == null)
            throw new InvalidOperationException();
        if (entity.Type == null || Type.GetType(entity.Type) is not Type type)
            throw new InvalidOperationException();

        return JsonSerializer.Deserialize(entity.Data, type, jsonOptions)!;
    }

    static EventData ToEventData(object e, int version, JsonSerializerOptions jsonOptions, params ITableEntity[] includes)
    {
        var type = e.GetType();
        var asm = type.Assembly.GetName();
        var properties = new
        {
            Data = JsonSerializer.Serialize(e, jsonOptions),
            DataVersion = asm.Version?.ToString(2) ?? UnknownVersionString,
            Type = $"{type.FullName}, {asm.Name}",
            Version = version,
        };

        return new EventData(
            StreamEventId.None,
            EventProperties.From(properties),
            EventIncludes.From(includes.Select(x => Include.InsertOrReplace(x))));
    }

    class EventEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = "";
        public string RowKey { get; set; } = "";
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string? Data { get; set; }
        public string? DataVersion { get; set; }
        public string? Type { get; set; }
        public int? Version { get; set; }
    }
}

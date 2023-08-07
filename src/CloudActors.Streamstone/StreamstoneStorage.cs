using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos.Table;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Streamstone;

namespace Devlooped.CloudActors;

public class StreamstoneOptions
{
    static readonly JsonSerializerOptions options = new()
    {
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PreferredObjectCreationHandling = System.Text.Json.Serialization.JsonObjectCreationHandling.Populate,
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

public class StreamstoneStorage : IGrainStorage
{
    // We cache table names to avoid running CreateIfNotExistsAsync on each access.
    readonly ConcurrentDictionary<string, Task<CloudTable>> tables = new();
    readonly CloudStorageAccount storage;
    readonly StreamstoneOptions options;

    public StreamstoneStorage(CloudStorageAccount storage, StreamstoneOptions? options = default)
        => (this.storage, this.options)
        = (storage, options ?? StreamstoneOptions.Default);

    public async Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        var table = await GetTable<T>(storage, stateName);
        await table.ExecuteAsync(TableOperation.Delete(new TableEntity(table.Name, grainId.Key.ToString()!)));
    }

    public async Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        var table = await GetTable<T>(storage, stateName);
        var rowId = grainId.Key.ToString();

        if (grainState.State is IEventSourced state)
        {
            var partition = new Partition(table, rowId);
            var stream = await Stream.TryOpenAsync(partition);
            if (!stream.Found)
            {
                state.LoadEvents(Enumerable.Empty<object>());
                grainState.ETag = "0";
                return;
            }

            if (options.AutoSnapshot)
            {
                // See if we can quickly load from most recent snapshot.
                var result = await table.ExecuteAsync(TableOperation.Retrieve<EventEntity>(table.Name, typeof(T).FullName));
                if (result.HttpStatusCode == 200 &&
                    result.Result is EventEntity entity &&
                    // We only apply snapshots where major.minor matches the current version, otherwise, 
                    // we might be losing important business logic changes.
                    typeof(T).Assembly.GetName() is { } asm &&
                    new Version(asm.Version?.Major ?? 0, asm.Version?.Minor ?? 0).ToString() == entity.DataVersion &&
                    entity.Data is string data &&
                    JsonSerializer.Deserialize<T>(data, options.JsonOptions) is { } instance)
                {
                    // Since auto-snapshotting is performed automatically and atomically on 
                    // every write, we don't need to replay any further events.
                    grainState.State = instance;
                    grainState.ETag = entity.Version.ToString();
                    grainState.RecordExists = true;
                    return;
                }
            }

            var entities = await Stream.ReadAsync<EventEntity>(partition);
            if (entities == null || !entities.HasEvents)
                return;

            // TODO: should we always re-create the state instance?
            state.LoadEvents(entities.Events.Select(ToDomainEvent));
            grainState.ETag = stream.Stream.Version.ToString();
            grainState.RecordExists = true;
        }
        else
        {
            var result = await table.ExecuteAsync(TableOperation.Retrieve<EventEntity>(table.Name, rowId));
            if (result.HttpStatusCode == 404 ||
                result.Result is not EventEntity entity ||
                entity.Data is not string data ||
                JsonSerializer.Deserialize<T>(data, options.JsonOptions) is not { } instance)
                return;

            grainState.State = instance;
            grainState.ETag = result.Etag;
            grainState.RecordExists = true;
        }
    }

    public async Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        var table = await GetTable<T>(storage, stateName);
        var rowId = grainId.Key.ToString();
        var type = typeof(T);
        var asm = typeof(T).Assembly.GetName();

        if (grainState.State is IEventSourced state)
        {
            // We never save unless we got events, which should be the only 
            // way to change state for an event-sourced object.
            if (state.Events.Count == 0)
                return;

            var partition = new Partition(table, grainId.Key.ToString());
            var result = await Stream.TryOpenAsync(partition);
            var stream = result.Found ? result.Stream : new Stream(partition);

            // Atomically write events + header
            try
            {
                var includes = options.AutoSnapshot ?
                    new ITableEntity[]
                    {
                        new EventEntity
                        {
                            PartitionKey = table.Name,
                            RowKey = typeof(T).FullName,
                            Data = JsonSerializer.Serialize(grainState.State, options.JsonOptions),
                            DataVersion = new Version(asm.Version?.Major ?? 0, asm.Version?.Minor ?? 0).ToString(),
                            Type = $"{type.FullName}, {asm.Name}",
                            Version = stream.Version + state.Events.Count
                        }
                    } : Array.Empty<ITableEntity>();

                await Stream.WriteAsync(partition,
                    int.TryParse(grainState.ETag, out var version) ? version : 0,
                    state.Events.Select((e, i) =>
                    ToEventData(e, stream.Version + i, includes)).ToArray());

                grainState.ETag = (stream.Version + state.Events.Count).ToString();
                grainState.RecordExists = true;
                state.AcceptEvents();
            }
            catch (ConcurrencyConflictException ce)
            {
                throw new InconsistentStateException(ce.Message);
            }
        }
        else
        {
            var result = await table.ExecuteAsync(TableOperation.InsertOrReplace(new EventEntity
            {
                PartitionKey = table.Name,
                RowKey = rowId,
                ETag = grainState.ETag,
                Data = JsonSerializer.Serialize(grainState.State, options.JsonOptions),
                DataVersion = new Version(asm.Version?.Major ?? 0, asm.Version?.Minor ?? 0).ToString(),
                Type = $"{type.FullName}, {asm.Name}",
            }));

            grainState.ETag = result.Etag;
            grainState.RecordExists = true;
        }
    }

    async Task<CloudTable> GetTable<T>(CloudStorageAccount storage, string name)
    {
        var getTable = tables.GetOrAdd(name, async key =>
        {
            var client = storage.CreateCloudTableClient();
            var table = client.GetTableReference(key);
            await table.CreateIfNotExistsAsync();
            return table;
        });

        return await getTable;
    }

    class EventEntity : TableEntity
    {
        public string? Data { get; set; }
        public string? DataVersion { get; set; }
        public string? Type { get; set; }
        public int? Version { get; set; }
    }

    object ToDomainEvent(EventEntity entity)
    {
        if (entity.Data == null)
            throw new InvalidOperationException();
        if (entity.Type == null || Type.GetType(entity.Type) is not Type type)
            throw new InvalidOperationException();

        // TODO: handle version mismatch between type and entity.DataVersion

        return JsonSerializer.Deserialize(entity.Data, type, options.JsonOptions)!;
    }

    EventData ToEventData(object e, int version, params ITableEntity[] includes)
    {
        var type = e.GetType();
        var asm = type.Assembly.GetName();
        // Properties are turned into columns in the table, which can be 
        // convenient for quickly glancing at the data.
        var properties = new
        {
            Data = JsonSerializer.Serialize(e, options.JsonOptions),
            DataVersion = new Version(asm.Version?.Major ?? 0, asm.Version?.Minor ?? 0).ToString(),
            // Visualizing the event id in the table as a column is useful for querying
            Type = $"{e.GetType().FullName}, {e.GetType().Assembly.GetName().Name}",
            Version = version,
        };

        return new EventData(
            // This turns off the SS-UID-[id] duplicate event detection rows, since we use 
            // single threaded 
            EventId.None,
            EventProperties.From(properties),
            EventIncludes.From(includes.Select(x => Include.InsertOrReplace(x))));
    }
}

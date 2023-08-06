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

public class StreamstoneStorage(CloudStorageAccount storage) : IGrainStorage
{
    static readonly JsonSerializerOptions options = new()
    {
        PreferredObjectCreationHandling = System.Text.Json.Serialization.JsonObjectCreationHandling.Populate
    };

    ConcurrentDictionary<string, Task<CloudTable>> tables = new();

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

            var entities = await Stream.ReadAsync<EventEntity>(partition);
            if (entities == null || !entities.HasEvents)
                return;

            var events = entities.Events.Select(ToDomainEvent).ToList();
            state.LoadEvents(events);
            grainState.ETag = stream.Stream.Version.ToString();
            grainState.RecordExists = true;
        }
        else
        {
            var result = await table.ExecuteAsync(TableOperation.Retrieve<EventEntity>(table.Name, rowId));
            if (result.HttpStatusCode == 404 || 
                result.Result is not EventEntity entity || 
                entity.Data is null ||
                // TODO: we could check for entity.DataVersion and see if it's compatible with T
                JsonSerializer.Deserialize<T>(entity.Data, options) is not T data)
                return;

            grainState.State = data;
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
            var header = new EventEntity
            {
                PartitionKey = table.Name,
                RowKey = typeof(T).FullName,
                Data = JsonSerializer.Serialize(grainState.State, options),
                DataVersion = new Version(asm.Version?.Major ?? 0, asm.Version?.Minor ?? 0).ToString(),
                Type = $"{type.FullName}, {asm.Name}",
                Version = stream.Version + state.Events.Count,
            };

            // Atomically write events + header
            try
            {
                await Stream.WriteAsync(partition,
                    int.TryParse(grainState.ETag, out var version) ? version : 0,
                    state.Events.Select((e, i) =>
                    ToEventData(e, stream.Version + i, header)).ToArray());

                state.AcceptEvents();
                grainState.ETag = header.Version.ToString();
                grainState.RecordExists = true;
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
                Data = JsonSerializer.Serialize(grainState.State, options),
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

    static object ToDomainEvent(EventEntity entity)
    {
        if (entity.Data == null)
            throw new InvalidOperationException();
        if (entity.Type == null || Type.GetType(entity.Type) is not Type type)
            throw new InvalidOperationException();

        // TODO: handle version mismatch between type and entity.DataVersion

        return JsonSerializer.Deserialize(entity.Data, type, options)!;
    }

    static EventData ToEventData(object e, int version, params ITableEntity[] includes)
    {
        var type = e.GetType();
        var asm = type.Assembly.GetName();
        // Properties are turned into columns in the table, which can be 
        // convenient for quickly glancing at the data.
        var properties = new
        {
            Data = JsonSerializer.Serialize(e, options),
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

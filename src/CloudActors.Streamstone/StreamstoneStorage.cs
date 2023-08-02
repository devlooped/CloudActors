using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CloudNative.CloudEvents;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.Documents;
using Orleans;
using Orleans.Runtime;
using Orleans.Serialization;
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
        var table = await GetTable<T>(storage, grainId);
        await table.ExecuteAsync(TableOperation.Delete(new TableEntity(table.Name, grainId.Key.ToString()!)));
    }
    
    public async Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        var table = await GetTable<T>(storage, grainId);
        var rowId = grainId.Key.ToString();

        if (grainState.State is IEventSourced state)
        {
            var partition = new Partition(table, rowId);
            var stream = await Stream.TryOpenAsync(partition);
            if (!stream.Found)
            {
                state.LoadEvents(Enumerable.Empty<object>());
                return;
            }

            var entities = await Stream.ReadAsync<EventEntity>(partition);
            if (entities == null || !entities.HasEvents)
                return;

            var events = entities.Events.Select(ToDomainEvent).ToList();
            state.LoadEvents(events);
        }
        else
        {
            var result = await table.ExecuteAsync(TableOperation.Retrieve<EventEntity>(table.Name, rowId));
            if (result.HttpStatusCode == 404)
                return;

            grainState.State = (T)result.Result;
        }
    }

    public async Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        var table = await GetTable<T>(storage, grainId);
        var rowId = grainId.Key.ToString();

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
                Type = $"{typeof(T).FullName}, {typeof(T).Assembly.GetName().Name}",
                Version = stream.Version + state.Events.Count,
            };

            // Atomically write events + header
            await Stream.WriteAsync(partition, state.Version, state.Events.Select((e, i) =>
                ToEventData(e, stream.Version + i, header)).ToArray());

            state.AcceptEvents();
        }
        else
        {
            await table.ExecuteAsync(TableOperation.InsertOrReplace(new EventEntity
            {
                PartitionKey = table.Name,
                RowKey = rowId,
                Data = JsonSerializer.Serialize(grainState.State, options),
                Type = $"{typeof(T).FullName}, {typeof(T).Assembly.GetName().Name}",
            }));
        }
    }

    string TypeName<T>(GrainId grainId) => grainId.Type.ToString() ?? typeof(T).Name.ToLowerInvariant();

    async Task<CloudTable> GetTable<T>(CloudStorageAccount storage, GrainId grainId)
    {
        var getTable = tables.GetOrAdd(TypeName<T>(grainId), async key =>
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
        public string? Type { get; set; }
        public string? Data { get; set; }
        public int? Version { get; set; }
    }

    static object ToDomainEvent(EventEntity entity)
    {
        if (entity.Data == null)
            throw new InvalidOperationException();
        if (entity.Type == null || Type.GetType(entity.Type) is not Type type)
            throw new InvalidOperationException();

        return JsonSerializer.Deserialize(entity.Data, type, options)!;
    }

    static EventData ToEventData(object e, int version, params ITableEntity[] includes)
    {
        // Properties are turned into columns in the table, which can be 
        // convenient for quickly glancing at the data.
        var properties = new
        {
            // Visualizing the event id in the table as a column is useful for querying
            Type = $"{e.GetType().FullName}, {e.GetType().Assembly.GetName().Name}",
            Data = JsonSerializer.Serialize(e, options),
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

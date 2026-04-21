using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Streamstone;
using StreamEventId = Streamstone.EventId;

namespace Devlooped.CloudActors;

/// <summary>Implements an event-source aware grain storage provider.</summary>
/// <remarks>
/// If the grain state implements <see cref="IEventSourced"/>, events will be stored in
/// streams, otherwise the state will be stored as a single entity per grain.
/// </remarks>
public class StreamstoneStorage : IGrainStorage
{
    static readonly Version UnknownVersion = new(0, 0);
    const string UnknownVersionString = "0.0";

    // We cache table names to avoid running CreateIfNotExistsAsync on each access.
    readonly ConcurrentDictionary<string, Task<TableClient>> tables = new();
    readonly ConcurrentDictionary<(string Name, GrainId GrainId), BackgroundWriter> writers = new();
    readonly CloudStorageAccount storage;
    readonly StreamstoneOptions options;
    readonly ILogger<StreamstoneStorage> logger;
    readonly IGrainContextAccessor? grainContextAccessor;

    /// <summary>Creates a new <see cref="StreamstoneStorage"/> instance.</summary>
    public StreamstoneStorage(CloudStorageAccount storage, StreamstoneOptions? options = default)
        : this(storage, options, NullLogger<StreamstoneStorage>.Instance, grainContextAccessor: null) { }

    /// <summary>Creates a new <see cref="StreamstoneStorage"/> instance with the specified logger and (optionally) grain context accessor.</summary>
    public StreamstoneStorage(
        CloudStorageAccount storage,
        StreamstoneOptions? options,
        ILogger<StreamstoneStorage> logger,
        IGrainContextAccessor? grainContextAccessor = null)
        => (this.storage, this.options, this.logger, this.grainContextAccessor)
        = (storage, options ?? StreamstoneOptions.Default, logger ?? NullLogger<StreamstoneStorage>.Instance, grainContextAccessor);

    /// <inheritdoc/>
    public async Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        if (writers.TryRemove((stateName, grainId), out var writer))
            writer.Dispose();

        var table = await GetTable(storage, stateName);
        await table.SubmitTransactionAsync([new TableTransactionAction(TableTransactionActionType.Delete, new TableEntity(table.Name, grainId.Key.ToString()!))]);
    }

    /// <inheritdoc/>
    public async Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        var table = await GetTable(storage, stateName);
        var rowId = grainId.Key.ToString();
        var jsonOptions = GetJsonOptions(grainState);

        if (grainState.State is IEventSourced state)
        {
            var result = await StreamstoneJournaledStorage.ReadStateAsync(
                name => GetTable(storage, name),
                options,
                stateName,
                grainId,
                jsonOptions,
                () => grainState.State!,
                (current, events) => ((IEventSourced)current!).LoadEvents(events));

            grainState.State = result.Value;
            grainState.ETag = result.Key.ToString();
            grainState.RecordExists = result.Key > 0;
        }
        else
        {
            var result = await table.GetEntityIfExistsAsync<EventEntity>(table.Name, rowId);
            if (!result.HasValue ||
                result.Value is not EventEntity entity ||
                entity.Data is not string data ||
                // TODO: how to deal with versioning in this case?
                JsonSerializer.Deserialize<T>(data, jsonOptions) is not { } instance)
                return;

            grainState.State = instance;
            grainState.ETag = entity.ETag.ToString("G");
            grainState.RecordExists = true;
        }
    }

    /// <inheritdoc/>
    public async Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        // Background-save fast path: only for event-sourced actors when explicitly enabled.
        // We snapshot the events synchronously on the grain scheduler, accept them on the actor
        // (so subsequent commands don't re-emit them), and let a per-grain BatchWorker drain
        // the queue in the background.
        if (options.BackgroundSave && grainState.State is IEventSourced eventSourced)
        {
            if (eventSourced.Events.Count == 0)
                return;

            var writer = writers.GetOrAdd((stateName, grainId),
                key => new BackgroundWriter(this, key.Name, key.GrainId, typeof(T)));

            writer.Submit(grainState, eventSourced);
            return;
        }

        await WriteStateCoreAsync(stateName, grainId, grainState);
    }

    async Task WriteStateCoreAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        var table = await GetTable(storage, stateName);
        var rowId = grainId.Key.ToString();
        var type = typeof(T);
        var asm = typeof(T).Assembly.GetName();
        var jsonOptions = GetJsonOptions(grainState);

        if (grainState.State is IEventSourced state)
        {
            // We never save unless we got events, which should be the only 
            // way to change state for an event-sourced object.
            if (state.Events.Count == 0)
                return;

            var version = int.TryParse(grainState.ETag, out var parsed) ? parsed : 0;
            var newVersion = await StreamstoneJournaledStorage.AppendEventsAsync(
                name => GetTable(storage, name),
                options,
                stateName,
                grainId,
                jsonOptions,
                version,
                state.Events,
                grainState.State!);

            if (newVersion is null)
                throw new InconsistentStateException("The grain state could not be updated due to a concurrency conflict.");

            grainState.ETag = newVersion.Value.ToString();
            grainState.RecordExists = true;
            state.AcceptEvents();
        }
        else
        {
            var result = await table.SubmitTransactionAsync([new TableTransactionAction(TableTransactionActionType.UpsertReplace, new EventEntity
            {
                PartitionKey = table.Name,
                RowKey = rowId!,
                ETag = new ETag(grainState.ETag),
                Data = JsonSerializer.Serialize(grainState.State, jsonOptions),
                DataVersion = new Version(asm.Version?.Major ?? 0, asm.Version?.Minor ?? 0).ToString(),
                Type = $"{type.FullName}, {asm.Name}",
            })]);

            grainState.ETag = result.Value[0].Headers.ETag?.ToString();
            grainState.RecordExists = true;
        }
    }

    /// <summary>
    /// Flushes any pending background writes for the specified grain. Useful from tests and from
    /// grain code that needs to await durability before responding to a caller.
    /// </summary>
    public Task FlushAsync(string stateName, GrainId grainId)
        => writers.TryGetValue((stateName, grainId), out var writer)
            ? writer.FlushAsync()
            : Task.CompletedTask;

    JsonSerializerOptions GetJsonOptions<T>(IGrainState<T> grainState)
        => (grainState.State as IActorState)?.JsonOptions ?? options.JsonOptions;

    async Task<TableClient> GetTable(CloudStorageAccount storage, string name)
    {
        var getTable = tables.GetOrAdd(name, async key =>
        {
            var client = storage.CreateCloudTableClient();
            var table = client.GetTableClient(key);
            await table.CreateIfNotExistsAsync();
            return table;
        });

        return await getTable;
    }

    bool IsCompatible(Version assemblyVersion, string? dataVersion)
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

    static object ToDomainEvent(EventEntity entity, JsonSerializerOptions jsonOptions)
    {
        if (entity.Data == null)
            throw new InvalidOperationException();
        if (entity.Type == null || Type.GetType(entity.Type) is not Type type)
            throw new InvalidOperationException();

        // TODO: handle version mismatch between type and entity.DataVersion

        return JsonSerializer.Deserialize(entity.Data, type, jsonOptions)!;
    }

    static EventData ToEventData(object e, int version, JsonSerializerOptions jsonOptions, params ITableEntity[] includes)
    {
        var type = e.GetType();
        var asm = type.Assembly.GetName();
        // Properties are turned into columns in the table, which can be 
        // convenient for quickly glancing at the data.
        var properties = new
        {
            Data = JsonSerializer.Serialize(e, jsonOptions),
            DataVersion = asm.Version?.ToString(2) ?? UnknownVersionString,
            // Visualizing the event id in the table as a column is useful for querying
            Type = $"{e.GetType().FullName}, {e.GetType().Assembly.GetName().Name}",
            Version = version,
        };

        return new EventData(
            // This turns off the SS-UID-[id] duplicate event detection rows, since we use are single threaded 
            StreamEventId.None,
            EventProperties.From(properties),
            EventIncludes.From(includes.Select(x => Include.InsertOrReplace(x))));
    }

    /// <summary>
    /// Per-grain background writer. Owns a <see cref="BatchWorker"/> that drains a pending queue
    /// of event submissions, batching them into a single <c>Stream.WriteAsync</c>. Subscribes to
    /// the grain's <see cref="IGrainContext.ObservableLifecycle"/> on first submission so the queue
    /// is flushed before deactivation. Forces grain deactivation on terminal write failure.
    /// </summary>
    sealed class BackgroundWriter : IDisposable
    {
        readonly StreamstoneStorage owner;
        readonly string stateName;
        readonly GrainId grainId;
        readonly Type stateType;
        readonly BatchWorker worker;
        readonly List<Submission> pending = new();
        IGrainContext? grainContext;
        IDisposable? lifecycleSubscription;
        JsonSerializerOptions? jsonOptions;
        Func<object?>? readState;
        Func<string?>? readETag;
        Action<string>? writeETag;
        Action? markRecordExists;
        bool faulted;

        public BackgroundWriter(StreamstoneStorage owner, string stateName, GrainId grainId, Type stateType)
        {
            this.owner = owner;
            this.stateName = stateName;
            this.grainId = grainId;
            this.stateType = stateType;
            worker = new BatchWorkerFromDelegate(WorkAsync);
        }

        public void Submit<T>(IGrainState<T> state, IEventSourced eventSourced)
        {
            // Snapshot the events at the moment of submission and accept them on the actor so
            // subsequent commands do not re-emit them.
            var events = eventSourced.Events.ToArray();
            eventSourced.AcceptEvents();

            jsonOptions ??= owner.GetJsonOptions(state);
            // Capture typed accessors so the worker can read/update the grainState without reflection.
            readState ??= () => state.State;
            readETag ??= () => state.ETag;
            writeETag ??= v => state.ETag = v;
            markRecordExists ??= () => state.RecordExists = true;

            // Capture the grain context once, on the grain scheduler, and subscribe to its
            // lifecycle so the queue is flushed before deactivation.
            if (grainContext == null && owner.grainContextAccessor?.GrainContext is { } ctx)
            {
                grainContext = ctx;
                lifecycleSubscription = ctx.ObservableLifecycle.Subscribe(
                    nameof(StreamstoneStorage),
                    GrainLifecycleStage.SetupState,
                    _ => Task.CompletedTask,
                    async _ => await FlushAsync().ConfigureAwait(false));
            }

            pending.Add(new Submission(events));
            worker.Notify();
        }

        public Task FlushAsync() => worker.WaitForCurrentWorkToBeServiced();

        public void Dispose()
        {
            lifecycleSubscription?.Dispose();
            lifecycleSubscription = null;
        }

        async Task WorkAsync()
        {
            if (faulted || pending.Count == 0 || readState == null || jsonOptions == null)
                return;

            // Drain everything currently queued into a single batch.
            var batch = pending.ToArray();
            pending.Clear();

            var totalEvents = batch.Sum(b => b.Events.Length);
            if (totalEvents == 0)
                return;

            var allEvents = batch.SelectMany(b => b.Events).ToArray();

            Exception? lastError = null;
            for (var attempt = 0; attempt < Math.Max(1, owner.options.BackgroundSaveMaxAttempts); attempt++)
            {
                try
                {
                    await WriteBatchAsync(allEvents).ConfigureAwait(false);
                    return;
                }
                catch (ConcurrencyConflictException cce)
                {
                    // Another writer won (rare for single-activation grains). Re-open the stream
                    // to refresh the version and retry once with the same batch.
                    lastError = cce;
                    owner.logger.LogWarning(cce,
                        "Streamstone background write conflict for grain {GrainId} (attempt {Attempt}). Refreshing stream and retrying.",
                        grainId, attempt + 1);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    owner.logger.LogWarning(ex,
                        "Streamstone background write failed for grain {GrainId} (attempt {Attempt}/{Max}).",
                        grainId, attempt + 1, owner.options.BackgroundSaveMaxAttempts);
                }

                if (attempt + 1 < owner.options.BackgroundSaveMaxAttempts)
                {
                    var delay = TimeSpan.FromMilliseconds(Math.Min(
                        owner.options.BackgroundSaveRetryDelay.TotalMilliseconds * Math.Pow(2, attempt),
                        TimeSpan.FromSeconds(30).TotalMilliseconds));
                    await Task.Delay(delay).ConfigureAwait(false);
                }
            }

            // Terminal failure: mark faulted, drop the batch, force grain deactivation so it
            // re-reads from storage on next activation. The in-memory events that we already
            // accepted on the actor are lost — storage is the source of truth from here on.
            faulted = true;
            owner.logger.LogError(lastError,
                "Streamstone background write permanently failed for grain {GrainId}. Forcing deactivation.",
                grainId);

            owner.writers.TryRemove((stateName, grainId), out _);

            try
            {
                grainContext?.Deactivate(
                    new DeactivationReason(DeactivationReasonCode.ApplicationError,
                        $"Streamstone background save failed: {lastError?.Message}"),
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                owner.logger.LogError(ex, "Failed to deactivate grain {GrainId} after background write failure.", grainId);
            }

            Dispose();
        }

        async Task WriteBatchAsync(object[] events)
        {
            var table = await owner.GetTable(owner.storage, stateName).ConfigureAwait(false);
            var partition = new Partition(table, grainId.Key.ToString());

            // Use the live ETag from grainState as the expected version. ReadStateAsync seeds it
            // on activation and we update it after every successful write below.
            var expectedVersion = int.TryParse(readETag!(), out var v) ? v : 0;

            var asm = stateType.Assembly.GetName();
            var includes = owner.options.AutoSnapshot ?
                [
                    new EventEntity
                    {
                        PartitionKey = table.Name,
                        RowKey = "State",
                        Data = JsonSerializer.Serialize(readState!(), stateType, jsonOptions),
                        DataVersion = asm.Version?.ToString(2) ?? UnknownVersionString,
                        Type = $"{stateType.FullName ?? stateType.Name}, {asm.Name}",
                        Version = expectedVersion + events.Length,
                    }
                ] : Array.Empty<ITableEntity>();

            var eventData = events
                .Select((e, i) => ToEventData(e, expectedVersion + i, jsonOptions!, includes))
                .ToArray();

            await Stream.WriteAsync(partition, expectedVersion, eventData).ConfigureAwait(false);

            writeETag!((expectedVersion + events.Length).ToString());
            markRecordExists!();
        }

        readonly struct Submission(object[] events)
        {
            public object[] Events { get; } = events;
        }
    }
}

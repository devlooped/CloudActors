using System;
using System.Threading.Tasks;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;

namespace Devlooped.CloudActors;

/// <summary>
/// Extension methods for <see cref="StreamstoneStorage"/> to read actor state without Orleans.
/// </summary>
public static class StreamstoneStorageExtensions
{
    /// <summary>
    /// Reads an actor's state from storage and returns a reconstructed actor instance.
    /// This method allows reading persisted actor state without spinning up Orleans.
    /// </summary>
    /// <typeparam name="TActor">The actor type to read.</typeparam>
    /// <param name="storage">The StreamstoneStorage instance.</param>
    /// <param name="id">The actor's unique identifier.</param>
    /// <param name="stateName">Optional state name override. If not provided, uses the actor type name.</param>
    /// <returns>A reconstructed actor instance with its persisted state, or null if not found.</returns>
    /// <remarks>
    /// This method works with both event-sourced and regular actors. For event-sourced actors,
    /// it will replay all events to reconstruct the state. The actor type must have a constructor
    /// that accepts a single string parameter (the id).
    /// </remarks>
    public static async Task<TActor?> ReadAsync<TActor>(this StreamstoneStorage storage, string id, string? stateName = null)
        where TActor : class
    {
        if (storage == null)
            throw new ArgumentNullException(nameof(storage));
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("Actor id cannot be null or empty.", nameof(id));

        var actorType = typeof(TActor);
        
        // Find the ActorState nested type
        var stateType = actorType.GetNestedType("ActorState");
        if (stateType == null)
            throw new InvalidOperationException($"Actor type {actorType.Name} does not have a nested ActorState class. Ensure the actor has the [Actor] attribute.");

        // Use the provided state name or default to the actor type name
        stateName = stateName ?? actorType.Name;

        // Create the actor instance first (needed for event-sourced actors)
        var constructor = actorType.GetConstructor(new[] { typeof(string) });
        if (constructor == null)
            throw new InvalidOperationException($"Actor type {actorType.Name} must have a constructor that accepts a single string parameter (the actor id).");

        var actor = (TActor)constructor.Invoke(new object[] { id });

        // Get the initial state from the actor
        var actorInterfaceType = typeof(IActor<>).MakeGenericType(stateType);
        var getStateMethod = actorInterfaceType.GetMethod("GetState");
        if (getStateMethod == null)
            throw new InvalidOperationException("Failed to find GetState method on IActor<TState>.");

        var initialState = getStateMethod.Invoke(actor, Array.Empty<object>());
        if (initialState == null)
            throw new InvalidOperationException($"GetState returned null for actor {actorType.Name}");

        // Check if this is an event-sourced actor
        bool isEventSourced = stateType.IsAssignableTo(typeof(IEventSourced));

        // For event-sourced actors, we need to set the __actor reference before reading
        if (isEventSourced)
        {
            var actorField = stateType.GetField("__actor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (actorField != null)
            {
                actorField.SetValue(initialState, actor);
            }
        }

        // Create a grain state wrapper
        var grainStateType = typeof(SimpleGrainState<>).MakeGenericType(stateType);
        var grainState = Activator.CreateInstance(grainStateType, initialState);
        
        if (grainState == null)
            throw new InvalidOperationException("Failed to create grain state instance.");

        // Construct the grain ID
        var grainId = GrainId.Parse($"{actorType.Name.ToLowerInvariant()}/{id}");

        // Read the state from storage using the IGrainStorage interface
        var readMethod = typeof(IGrainStorage).GetMethod(nameof(IGrainStorage.ReadStateAsync));
        if (readMethod == null)
            throw new InvalidOperationException("Failed to find ReadStateAsync method on IGrainStorage.");

        var genericReadMethod = readMethod.MakeGenericMethod(stateType);
        try
        {
            var readTask = (Task)genericReadMethod.Invoke(storage, new object?[] { stateName, grainId, grainState })!;
            await readTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to read state for actor {actorType.Name} with id {id} from state {stateName}: {ex.Message}", ex);
        }

        // Get the record exists property
        var recordExistsProperty = grainStateType.GetProperty("RecordExists");
        var recordExists = (bool)(recordExistsProperty?.GetValue(grainState) ?? false);

        // If no record exists, return null
        if (!recordExists)
            return null;

        // Get the loaded state from storage
        var stateProperty = grainStateType.GetProperty("State");
        var currentState = stateProperty?.GetValue(grainState);
        
        if (currentState == null)
            throw new InvalidOperationException($"State value is null for actor {actorType.Name} with id {id}");

        // Always call SetState to sync the loaded state to the actor
        // For event-sourced actors:
        //   - If snapshot was loaded: currentState has correct values from snapshot
        //   - If events were replayed: currentState may have stale values, but we'll handle that
        var setStateMethod = actorInterfaceType.GetMethod("SetState");
        if (setStateMethod == null)
            throw new InvalidOperationException("Failed to find SetState method on IActor<TState>.");

        try
        {
            setStateMethod.Invoke(actor, new[] { currentState });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to set state on actor {actorType.Name}: {ex.Message}", ex);
        }

        return actor;
    }

    // Helper class to implement IGrainState
    private class SimpleGrainState<T> : IGrainState<T>
    {
        public SimpleGrainState() => State = default!;
        public SimpleGrainState(T state) => State = state;
        public T State { get; set; }
        public string ETag { get; set; } = "";
        public bool RecordExists { get; set; }
    }
}

using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;

namespace Devlooped.CloudActors;

/// <summary>Extensions for <see cref="IGrainStorage"/> to read actor instances.</summary>
public static class GrainStorageExtensions
{
    /// <summary>Reads an actor instance from storage.</summary>
    /// <typeparam name="TActor">The actor type implementing (via codegen) the <see cref="IActor{TState}"/> interface.</typeparam>
    /// <returns>The actor instance.</returns>
    public static Task<TActor> ReadActorAsync<TActor>(this IGrainStorage storage, string id)
    {
        var attr = typeof(TActor).GetCustomAttribute<ActorAttribute>() ??
            throw new ArgumentException($"{typeof(TActor).FullName} is not annotated with {nameof(ActorAttribute)}.");

        var actorState = typeof(IActor<>);
        var iface = typeof(TActor).GetInterfaces().FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == actorState)
            ?? throw new ArgumentException($"{typeof(TActor).FullName} does not implement IActor<TState>.");

        var stateType = iface.GetGenericArguments()[0];
        var method = typeof(GrainStorageExtensions).GetMethod(nameof(ReadActorAsync), 2, [typeof(IGrainStorage), typeof(string)])!
            .MakeGenericMethod(typeof(TActor), stateType);

        return (Task<TActor>)method.Invoke(null, [storage, id])!;
    }

    /// <summary>Reads an actor instance from storage.</summary>
    /// <typeparam name="TActor">The actor type implementing (via codegen) the <see cref="IActor{TState}"/> interface.</typeparam>
    /// <typeparam name="TState">The actor state type implementing (via codegen) the <see cref="IActorState{TActor}"/> interface.</typeparam>
    /// <returns>The actor instance.</returns>
    public static async Task<TActor> ReadActorAsync<TActor, TState>(this IGrainStorage storage, string id)
        where TActor : IActor<TState>
        where TState : IActorState<TActor>, new()
    {
        var attr = typeof(TActor).GetCustomAttribute<ActorAttribute>() ??
            throw new ArgumentException($"{typeof(TActor).FullName} is not annotated with {nameof(ActorAttribute)}.");

        var grainId = GrainId.Parse(id);
        var grainState = new GrainState<TState>(new());
        var stateName = attr.StateName ?? typeof(TActor).Name;

        await storage.ReadStateAsync(stateName, grainId, grainState);

        if (Activator.CreateInstance(typeof(TActor), [grainId.Key.ToString()]) is not TActor actor)
            throw new InvalidOperationException($"Could not create instance of actor type {typeof(TActor).FullName}.");

        actor.SetState(grainState.State);
        return actor;
    }
}

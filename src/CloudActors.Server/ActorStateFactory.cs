using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Core;
using Orleans.Runtime;

namespace Devlooped.CloudActors;

interface IActorStateFactory : IPersistentStateFactory { }

class ActorStateFactory(IPersistentStateFactory factory) : IActorStateFactory
{
    ConcurrentDictionary<Type, Delegate> stateFactories = new();

    public IPersistentState<TState> Create<TState>(IGrainContext context, IPersistentStateConfiguration config)
    {
        // We're super conservative here, only replacing if all conditions are met.

        var state = factory.Create<TState>(context, config);
        var stateType = typeof(TState);

        if (!stateType.IsAssignableTo(typeof(IActorState)) ||
            // We only know how the state bridge works, regarding rehydration
            state is not StateStorageBridge<TState> bridge ||
            stateType.GetInterfaces().FirstOrDefault(iface =>
                iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IActorState<>)) is not { } stateIface)
            return state;

        var actorType = stateIface.GetGenericArguments()[0];
        // This triggers our analyzers which would ensure requirements are satisfied.
        if (actorType.GetCustomAttribute<ActorAttribute>() is null)
            return state;

        var actorIface = typeof(IActor<>).MakeGenericType(typeof(TState));
        if (!actorIface.IsAssignableFrom(actorType))
            return state;

        if (context.GrainId.Key.ToString() is not object id)
            return state;

        try
        {
            if (ActivatorUtilities.CreateInstance(context.ActivationServices, actorType, id) is not { } actor)
                return state;

            if (Activator.CreateInstance(typeof(ActorPersistentState<,>).MakeGenericType(typeof(TState), actorType), actor, state) is not IPersistentState<TState> actorState)
                return state;

            return actorState;
        }
        catch (TargetInvocationException)
        {
            throw;
        }

        // Internally, this causes the StateStorageBridge<T>._grainState to not be 
        // forcedly constructed via Activator.CreateInstance with a parameterless constructor, 
        // and instead our "rehydrated" type is used instead.
        // NOTE: this causes the OnStart on the PersistentState<TState> class to skip invoking 
        // ReadStateAsync, as it assumes rehydration makes that unnecessary. 
        // However, the JournaledGrain base class overrides the OnActivateAsync method and 
        // forces a sync, so we're good since our generated grain does that too.
        //bridge.OnRehydrate(new ActivationContext(new GrainState<TState>(actor)));

    }

    class ActorPersistentState<TState, TActor> : IActorPersistentState<TState, TActor>
        where TActor : IActor<TState>
    {
        readonly IPersistentState<TState> persistence;

        public ActorPersistentState(TActor actor, IPersistentState<TState> state)
        {
            Actor = actor;
            this.persistence = state;
        }

        public TActor Actor { get; set; }

        public TState State => persistence.State;

        public string Etag => persistence.Etag;

        public bool RecordExists => persistence.RecordExists;

        TState IStorage<TState>.State
        {
            get => persistence.State;
            set => persistence.State = Actor.SetState(value);
        }

        public Task ClearStateAsync() => persistence.ClearStateAsync()
                .ContinueWith(t => Actor.SetState(persistence.State), TaskContinuationOptions.OnlyOnRanToCompletion);

        public Task ReadStateAsync() => persistence.ReadStateAsync()
                .ContinueWith(t =>
                {
                    // Don't overwrite state that may already have initial values from actor constructor.
                    if (persistence.RecordExists)
                        Actor.SetState(persistence.State);
                }, TaskContinuationOptions.OnlyOnRanToCompletion);

        public Task WriteStateAsync()
        {
            persistence.State = Actor.GetState();
            return persistence.WriteStateAsync();
        }
    }

    class ActivationContext(object actor) : IRehydrationContext
    {
        public IEnumerable<string> Keys => throw new NotImplementedException();
        public bool TryGetBytes(string key, out ReadOnlySequence<byte> value) => throw new NotImplementedException();
        public bool TryGetValue<T>(string key, out T? value)
        {
            if (actor is T typed)
            {
                value = typed;
                return true;
            }

            value = default;
            return false;
        }
    }
}

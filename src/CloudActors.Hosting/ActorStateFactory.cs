using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Core;
using Orleans.Runtime;

namespace Devlooped.CloudActors;

interface IActorStateFactory : IPersistentStateFactory { }

class ActorStateFactory(IPersistentStateFactory factory, IActorIdFactory idFactory) : IActorStateFactory
{
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

        if (context.GrainId.Key.ToString() is not { } key)
            return state;

        try
        {
            var id = idFactory.Create(actorType, key);
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

    }

    class ActorPersistentState<TState, TActor>(TActor actor, IPersistentState<TState> persistence) : IActorPersistentState<TState, TActor>
        where TActor : IActor<TState>
    {
        public TActor Actor { get; set; } = actor;

        public TState State => persistence.State;

        public string Etag => persistence.Etag ?? "";

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
                    else if (persistence.State is IConfirmableEvents stateConfirmable &&
                        Actor is IConfirmableEvents actorConfirmable)
                        actorConfirmable.ConfirmEventsCallback = stateConfirmable.ConfirmEventsCallback;
                }, TaskContinuationOptions.OnlyOnRanToCompletion);

        public Task WriteStateAsync()
        {
            persistence.State = Actor.GetState();
            return persistence.WriteStateAsync();
        }
    }
}

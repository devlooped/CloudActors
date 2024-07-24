﻿using System;
using System.Buffers;
using System.Collections.Generic;
using Orleans;
using Orleans.Core;
using Orleans.Runtime;

interface IActorStateFactory : IPersistentStateFactory { }

class ActorStateFactory(IPersistentStateFactory factory) : IActorStateFactory
{
    public IPersistentState<TState> Create<TState>(IGrainContext context, IPersistentStateConfiguration config)
    {
        var state = factory.Create<TState>(context, config);
        // We're super conservative here, only replacing if all conditions are met.
        // We check for a state with no parameterless constructor up-front, then go from there.
        if (typeof(TState).GetConstructor(Type.EmptyTypes) is null &&
            // We only know how the state bridge works
            state is StateStorageBridge<TState> bridge &&
            // We only support a ctor that receives the grain id as a string
            context.GrainId.Key.ToString() is object id &&
            typeof(TState).GetConstructor([typeof(string)]) is not null &&
            // Even so, we might fail activation
            Activator.CreateInstance(typeof(TState), id) is TState actor)
        {
            // In this case, we can force custom creation of the state object.
            // Internally, this causes the StateStorageBridge<T>._grainState to not be 
            // forcedly constructed via Activator.CreateInstance with a parameterless constructor, 
            // and instead our "rehydrated" type is used instead.
            // NOTE: this causes the OnStart on the PersistentState<TState> class to skip invoking 
            // ReadStateAsync, as it assumes rehydration makes that unnecessary. 
            // However, the JournaledGrain base class overrides the OnActivateAsync method and 
            // forces a sync, so we're good since our generated grain does that too.
            bridge.OnRehydrate(new ActivationContext(new GrainState<TState>(actor)));
        }

        return state;
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

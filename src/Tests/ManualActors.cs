using Devlooped.CloudActors;
using System.Threading.Tasks;
using System.Threading;
using System;
using Orleans;
using Orleans.Runtime;
using Tests;
using Orleans.Concurrency;
using Orleans.Core;

namespace PlainOrleans;

public interface IActorPersistentState<TState, TActor> : IPersistentState<TState>
{
    TActor Actor { get; set; }
}

class ActorPersistentState<TState, TActor> : IActorPersistentState<TState, TActor>
    where TActor : IActor<TState>
{
    IPersistentState<TState> persistence;

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
            .ContinueWith(t => Actor.SetState(persistence.State));

    public Task ReadStateAsync() => persistence.ReadStateAsync()
            .ContinueWith(t => Actor.SetState(persistence.State));

    public Task WriteStateAsync()
    {
        persistence.State = Actor.GetState();
        return persistence.WriteStateAsync();
    }
}

public partial record Increment() : IActorCommand;

public class MyActor : IActor<MyActor.ActorState>
{
    readonly IServiceProvider services;
    int count;

    public MyActor(string id, IServiceProvider services)
    {
        Id = id;
        this.services = services;
    }

    public string Id { get; }

    public void Increment(Increment _) => count++;

    ActorState? state;

    ActorState IActor<ActorState>.GetState()
    {
        state ??= new ActorState();
        state.Count = count;
        return state;
    }

    ActorState IActor<ActorState>.SetState(ActorState state)
    {
        this.state = state;
        count = state.Count;
        return state;
    }

    [GenerateSerializer]
    public class ActorState : IActorState<MyActor>
    {
        public MyActor Actor { get; set; }

        public int Count;
    }
}

public class MyActorGrain : Grain, IGrainWithStringKey
{
    readonly IActorPersistentState<MyActor.ActorState, MyActor> storage;

    public MyActorGrain([PersistentState("MyActor")] IPersistentState<MyActor.ActorState> storage)
        => this.storage = storage as IActorPersistentState<MyActor.ActorState, MyActor> ?? 
            throw new ArgumentException("Unsupported persistent state");

    [ReadOnly]
    public Task<TResult> QueryAsync<TResult>(IActorQuery<TResult> command)
    {
        switch (command)
        {
            default:
                throw new NotSupportedException();
        }
    }

    public Task<TResult> ExecuteAsync<TResult>(IActorCommand<TResult> command)
    {
        switch (command)
        {
            default:
                throw new NotSupportedException();
        }
    }

    public Task ExecuteAsync(IActorCommand command)
    {
        switch (command)
        {
            case Increment cmd:
                storage.Actor.Increment(cmd);
                return storage.WriteStateAsync();
            default:
                throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Just like the JournaledGrain, upon activation, we read the state from storage.
    /// </summary>
    public override Task OnActivateAsync(CancellationToken cancellationToken) => storage.ReadStateAsync();
}
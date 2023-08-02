using System;
using System.Threading.Tasks;
using Orleans;
using Orleans.Runtime;

namespace Devlooped.CloudActors;

public class OrleansActorBus(IGrainFactory factory) : IActorBus
{
    public Task<TResult> ExecuteAsync<TResult>(string id, IActorCommand<TResult> command) 
        => GetActor(id).ExecuteAsync(command);

    public Task ExecuteAsync(string id, IActorCommand command) 
        => GetActor(id).ExecuteAsync(command);

    public Task<TResult> QueryAsync<TResult>(string id, IActorQuery<TResult> query)
        => GetActor(id).QueryAsync(query);

    IActorGrain GetActor(string id) => factory.GetGrain<IActorGrain>(GrainId.Parse(id));
}

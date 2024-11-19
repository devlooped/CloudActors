using System.Threading.Tasks;
using Orleans;
using Orleans.Runtime;

namespace Devlooped.CloudActors;

/// <summary>
/// Implements the <see cref="IActorBus"/> interface over an <see cref="IGrainFactory"/>.
/// </summary>
public class OrleansActorBus(IGrainFactory factory) : IActorBus
{
    /// <inheritdoc/>
    public Task<TResult> ExecuteAsync<TResult>(string id, IActorCommand<TResult> command)
        => GetActor(id).ExecuteAsync(command);

    /// <inheritdoc/>
    public Task ExecuteAsync(string id, IActorCommand command)
        => GetActor(id).ExecuteAsync(command);

    /// <inheritdoc/>
    public Task<TResult> QueryAsync<TResult>(string id, IActorQuery<TResult> query)
        => GetActor(id).QueryAsync(query);

    IActorGrain GetActor(string id) => factory.GetGrain<IActorGrain>(GrainId.Parse(id));
}

using System.ComponentModel;
using System.Threading.Tasks;
using Orleans;

namespace Devlooped.CloudActors;

/// <summary>Interface for all generated actor grains.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IActorGrain : IGrainWithStringKey
{
    /// <summary>Executes the specified command against the actor.</summary>
    Task ExecuteAsync(IActorCommand command);
    /// <summary>Queries the specified value-returning command an against the actor.</summary>
    Task<TResult> ExecuteAsync<TResult>(IActorCommand<TResult> command);
    /// <summary>Queries the specified query against the actor.</summary>
    Task<TResult> QueryAsync<TResult>(IActorQuery<TResult> query);
}

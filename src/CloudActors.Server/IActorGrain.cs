using System.Threading.Tasks;
using Orleans;

namespace Devlooped.CloudActors;

public interface IActorGrain : IGrainWithStringKey
{
    Task ExecuteAsync(IActorCommand command);
    Task<TResult> ExecuteAsync<TResult>(IActorCommand<TResult> command);
    Task<TResult> QueryAsync<TResult>(IActorQuery<TResult> query);
}

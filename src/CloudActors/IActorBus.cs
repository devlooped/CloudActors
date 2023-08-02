using System.Threading.Tasks;

namespace Devlooped.CloudActors;

public interface IActorBus
{
    Task ExecuteAsync(string id, IActorCommand command);
    Task<TResult> ExecuteAsync<TResult>(string id, IActorCommand<TResult> command);
    Task<TResult> QueryAsync<TResult>(string id, IActorQuery<TResult> query);
}

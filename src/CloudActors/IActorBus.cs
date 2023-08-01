using System.Threading.Tasks;

namespace Devlooped.CloudActors;

public interface IActorBus
{
    Task<TResult> ExecuteAsync<TResult>(string id, IActorCommand<TResult> command);
    Task ExecuteAsync(string id, IActorCommand command);
    Task SendAsync(string id, IActorCommand command);
}

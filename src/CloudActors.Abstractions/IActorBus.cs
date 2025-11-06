using System.Threading.Tasks;

namespace Devlooped.CloudActors;

/// <summary>
/// Main interface for interacting with actors.
/// </summary>
public interface IActorBus
{
    /// <summary>
    /// Invokes a state-changing operation on an actor.
    /// </summary>
    /// <param name="id">The actor identifier, such as <c>account/1</c>.</param>
    /// <param name="command">The command to execute.</param>
    Task ExecuteAsync(string id, IActorCommand command);

    /// <summary>
    /// Invokes a state-changing operation on an actor which returns a value.
    /// </summary>
    /// <typeparam name="TResult">Type of returned value, typically inferred from the <paramref name="command"/>.</typeparam>
    /// <param name="id">The actor identifier, such as <c>account/1</c>.</param>
    /// <param name="command">The command to execute.</param>
    Task<TResult> ExecuteAsync<TResult>(string id, IActorCommand<TResult> command);

    /// <summary>
    /// Invokes a read-only query on an actor.
    /// </summary>
    /// <typeparam name="TResult">Type of returned value, typically inferred from the <paramref name="query"/>.</typeparam>
    /// <param name="id">The actor identifier, such as <c>account/1</c>.</param>
    /// <param name="query">The query to execute.</param>
    Task<TResult> QueryAsync<TResult>(string id, IActorQuery<TResult> query);
}

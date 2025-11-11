using System.ComponentModel;
using System.Runtime.CompilerServices;
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
    Task ExecuteAsync(string id, IActorCommand command) => ExecuteAsync(id, command, null, null, null);

    /// <summary>
    /// Invokes a state-changing operation on an actor which returns a value.
    /// </summary>
    /// <typeparam name="TResult">Type of returned value, typically inferred from the <paramref name="command"/>.</typeparam>
    /// <param name="id">The actor identifier, such as <c>account/1</c>.</param>
    /// <param name="command">The command to execute.</param>
    Task<TResult> ExecuteAsync<TResult>(string id, IActorCommand<TResult> command) => ExecuteAsync(id, command, null, null, null);

    /// <summary>
    /// Invokes a read-only query on an actor.
    /// </summary>
    /// <typeparam name="TResult">Type of returned value, typically inferred from the <paramref name="query"/>.</typeparam>
    /// <param name="id">The actor identifier, such as <c>account/1</c>.</param>
    /// <param name="query">The query to execute.</param>
    Task<TResult> QueryAsync<TResult>(string id, IActorQuery<TResult> query) => QueryAsync(id, query, null, null, null);

    // Hidden, prioritized overload for automatic telemetry capture.
    [EditorBrowsable(EditorBrowsableState.Never)]
    [OverloadResolutionPriority(1)]
    /// <summary>
    /// Invokes a state-changing operation on an actor.
    /// </summary>
    /// <param name="id">The actor identifier, such as <c>account/1</c>.</param>
    /// <param name="command">The command to execute.</param>
    /// <param name="callerName">Optional calling member name, provided by default by the compiler.</param>
    /// <param name="callerFile">Optional calling file name, provided by default by the compiler.</param>
    /// <param name="callerLine">Optional calling line number, provided by default by the compiler.</param>
    Task ExecuteAsync(string id, IActorCommand command, [CallerMemberName] string? callerName = default, [CallerFilePath] string? callerFile = default, [CallerLineNumber] int? callerLine = default);

    // Hidden, prioritized overload for automatic telemetry capture.
    [EditorBrowsable(EditorBrowsableState.Never)]
    [OverloadResolutionPriority(1)]
    /// <summary>
    /// Invokes a state-changing operation on an actor which returns a value.
    /// </summary>
    /// <typeparam name="TResult">Type of returned value, typically inferred from the <paramref name="command"/>.</typeparam>
    /// <param name="id">The actor identifier, such as <c>account/1</c>.</param>
    /// <param name="command">The command to execute.</param>
    /// <param name="callerName">Optional calling member name, provided by default by the compiler.</param>
    /// <param name="callerFile">Optional calling file name, provided by default by the compiler.</param>
    /// <param name="callerLine">Optional calling line number, provided by default by the compiler.</param>
    Task<TResult> ExecuteAsync<TResult>(string id, IActorCommand<TResult> command, [CallerMemberName] string? callerName = default, [CallerFilePath] string? callerFile = default, [CallerLineNumber] int? callerLine = default);

    // Hidden, prioritized overload for automatic telemetry capture.
    [EditorBrowsable(EditorBrowsableState.Never)]
    [OverloadResolutionPriority(1)]
    /// <summary>
    /// Invokes a read-only query on an actor.
    /// </summary>
    /// <typeparam name="TResult">Type of returned value, typically inferred from the <paramref name="query"/>.</typeparam>
    /// <param name="id">The actor identifier, such as <c>account/1</c>.</param>
    /// <param name="query">The query to execute.</param>
    /// <param name="callerName">Optional calling member name, provided by default by the compiler.</param>
    /// <param name="callerFile">Optional calling file name, provided by default by the compiler.</param>
    /// <param name="callerLine">Optional calling line number, provided by default by the compiler.</param>
    Task<TResult> QueryAsync<TResult>(string id, IActorQuery<TResult> query, [CallerMemberName] string? callerName = default, [CallerFilePath] string? callerFile = default, [CallerLineNumber] int? callerLine = default);
}

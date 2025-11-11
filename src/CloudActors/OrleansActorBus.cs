using System;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Orleans;
using Orleans.Runtime;
using static Devlooped.CloudActors.Telemetry;

namespace Devlooped.CloudActors;

/// <summary>
/// Implements the <see cref="IActorBus"/> interface over an <see cref="IGrainFactory"/>.
/// </summary>
public class OrleansActorBus(IGrainFactory factory) : IActorBus
{
    /// <inheritdoc/>
    public async Task<TResult> ExecuteAsync<TResult>(string id, IActorCommand<TResult> command, [CallerMemberName] string? callerName = default, [CallerFilePath] string? callerFile = default, [CallerLineNumber] int? callerLine = default)
    {
        using var activity = StartCommandActivity(command, callerName, callerFile, callerLine);

        try
        {
            return await GetActor(id).ExecuteAsync(command);
        }
        catch (Exception e)
        {
            activity.SetException(e);
            // Rethrow original exception to preserve stacktrace.
            ExceptionDispatchInfo.Capture(e).Throw();
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(string id, IActorCommand command, [CallerMemberName] string? callerName = default, [CallerFilePath] string? callerFile = default, [CallerLineNumber] int? callerLine = default)
    {
        using var activity = StartCommandActivity(command, callerName, callerFile, callerLine);

        try
        {
            await GetActor(id).ExecuteAsync(command);
        }
        catch (Exception e)
        {
            activity.SetException(e);
            // Rethrow original exception to preserve stacktrace.
            ExceptionDispatchInfo.Capture(e).Throw();
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<TResult> QueryAsync<TResult>(string id, IActorQuery<TResult> query, [CallerMemberName] string? callerName = default, [CallerFilePath] string? callerFile = default, [CallerLineNumber] int? callerLine = default)
    {
        using var activity = StartQueryActivity(query, callerName, callerFile, callerLine);

        try
        {
            return await GetActor(id).QueryAsync(query);
        }
        catch (Exception e)
        {
            activity.SetException(e);
            // Rethrow original exception to preserve stacktrace.
            ExceptionDispatchInfo.Capture(e).Throw();
            throw;
        }
    }

    IActorGrain GetActor(string id) => factory.GetGrain<IActorGrain>(GrainId.Parse(id));
}

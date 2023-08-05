namespace Devlooped.CloudActors;

/// <summary>
/// Base marker interface for both commands and queries.
/// </summary>
public interface IActorMessage { }

/// <summary>
/// Marker interface for void actor commands.
/// </summary>
public interface IActorCommand : IActorMessage { }

/// <summary>
/// Marker interface for value-returning actor commands.
/// </summary>
public interface IActorCommand<out TResult> : IActorCommand { }
namespace Devlooped.CloudActors;

/// <summary>
/// Marker interface for void actor commands.
/// </summary>
public interface IActorCommand : IActorMessage { }

/// <summary>
/// Marker interface for value-returning actor commands.
/// </summary>
public interface IActorCommand<out TResult> : IActorCommand { }
namespace Devlooped.CloudActors;

/// <summary>
/// Marker interface for actor queries.
/// </summary>
public interface IActorQuery<out TResult> : IActorMessage { }
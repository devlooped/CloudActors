using System.ComponentModel;

namespace Devlooped.CloudActors;

/// <summary>
/// Marker interface for actor state.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IActorState { }

/// <summary>
/// Generic version of the marker interface for state so we can track back its owning actor type.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IActorState<TActor> : IActorState
{
}
using System.ComponentModel;
using Orleans.Runtime;

namespace Devlooped.CloudActors;

/// <summary>
/// Allows exposing both the actor and its state to the grain. Instantiated by the <see cref="ActorStateFactory"/>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IActorPersistentState<TState, TActor> : IPersistentState<TState>
{
    /// <summary>
    /// Gets or sets the actor instance associated with this state.
    /// </summary>
    TActor Actor { get; set; }
}
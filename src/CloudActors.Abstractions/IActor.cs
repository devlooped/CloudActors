using System.ComponentModel;

namespace Devlooped.CloudActors;


/// <summary>
/// Implements the memento pattern for state retrieval and restoring.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IActor<TState>
{
    /// <summary>
    /// Gets the actor state.
    /// </summary>
    /// <returns></returns>
    TState GetState();
    /// <summary>
    /// Sets the actor state.
    /// </summary>
    TState SetState(TState state);
}
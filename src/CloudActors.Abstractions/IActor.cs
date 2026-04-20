using System.ComponentModel;

namespace Devlooped.CloudActors;

/// <summary>Implements the memento pattern for state retrieval and restoring.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IActor<TState>
{
    /// <summary>Gets the actor state.</summary>
    /// <returns>The current state of the actor.</returns>
    TState GetState();

    /// <summary>Sets the actor state.</summary>
    /// <param name="state">The new state to set.</param>
    /// <returns>The updated state of the actor.</returns>
    TState SetState(TState state);
}
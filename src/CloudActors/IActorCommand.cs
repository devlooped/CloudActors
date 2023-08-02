using System.ComponentModel;

namespace Devlooped.CloudActors;

/// <summary>
/// Marker interface for void actor commands.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IActorCommand { }

/// <summary>
/// Marker interface for value-returning actor commands.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IActorCommand<out TResult> : IActorCommand { }
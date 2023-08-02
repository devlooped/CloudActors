using System.ComponentModel;

namespace Devlooped.CloudActors;

/// <summary>
/// Marker interface for actor queries.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IActorQuery<out TResult> { }
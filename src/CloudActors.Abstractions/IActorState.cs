using System.ComponentModel;
using System.Text.Json;

namespace Devlooped.CloudActors;

/// <summary>Marker interface for actor state.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IActorState
{
    /// <summary>
    /// Gets the source-generated <see cref="JsonSerializerOptions"/> for this state type, 
    /// or <see langword="null"/> to use the default options from the storage provider.
    /// </summary>
    JsonSerializerOptions? JsonOptions => null;
}

/// <summary>Generic version of the marker interface for state so we can track back its owning actor type.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IActorState<TActor> : IActorState
{
}
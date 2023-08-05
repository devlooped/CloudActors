using System;

namespace Devlooped.CloudActors;

/// <summary>
/// Flags the decorated class as an actor.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class ActorAttribute : Attribute
{
    /// <summary>
    /// The actor uses the type name of the annotated class as the state 
    /// name and the default storage provider.
    /// </summary>
    public ActorAttribute() { }

    /// <summary>
    /// The actor uses the given state name and the default storage provider.
    /// </summary>
    public ActorAttribute(string stateName) { }

    /// <summary>
    /// The actor uses the given state name and storage provider.
    /// </summary>
    public ActorAttribute(string stateName, string storageName) { }
}
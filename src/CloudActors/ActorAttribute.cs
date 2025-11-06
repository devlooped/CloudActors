using System;

namespace Devlooped.CloudActors;

/// <summary>Flags the decorated class as an actor.</summary>
[AttributeUsage(AttributeTargets.Class)]
public class ActorAttribute : Attribute
{
    /// <summary>The actor uses the type name of the annotated class as the state name and the default storage provider.</summary>
    public ActorAttribute() { }

    /// <summary>The actor uses the given state name and the default storage provider.</summary>
    /// <param name="stateName">Use a state name other than the type name.</param>
    public ActorAttribute(string stateName) => StateName = stateName;

    /// <summary>The actor uses the given state name and storage provider.</summary>
    /// <param name="stateName">Use a state name other than the type name.</param>
    /// <param name="storageProvider">Storage provider name registered with Orleans.</param>
    public ActorAttribute(string? stateName = default, string? storageProvider = default)
    {
        StateName = stateName;
        StorageProvider = storageProvider;
    }

    /// <summary>State name to use instead of the type name.</summary>
    public string? StateName { get; }

    /// <summary>Storage provider name to use instead of the default one.</summary>
    public string? StorageProvider { get; }
}
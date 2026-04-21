using System;

namespace Devlooped.CloudActors;

/// <summary>Opts an actor into the JournaledGrain-based template.</summary>
[AttributeUsage(AttributeTargets.Class)]
public class JournaledAttribute : Attribute
{
    /// <summary>Creates a journaled actor declaration using the default provider name.</summary>
    public JournaledAttribute() { }

    /// <summary>Creates a journaled actor declaration using the specified provider name.</summary>
    /// <remarks>Silo configuration for this provider name is required and will determine the type of backend used.</remarks>
    public JournaledAttribute(string providerName) => ProviderName = providerName;

    /// <summary>Log consistency provider used by the generated journaled grain.</summary>
    /// <remarks>Silo configuration for this provider name is required and will determine the type of backend used.</remarks>
    public string? ProviderName { get; }
}

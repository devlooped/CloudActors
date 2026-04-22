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

    /// <summary>Creates a journaled actor declaration with the specified confirmation mode.</summary>
    public JournaledAttribute(bool backgroundSave) => BackgroundSave = backgroundSave;

    /// <summary>Creates a journaled actor declaration using the specified provider name and confirmation mode.</summary>
    /// <remarks>Silo configuration for this provider name is required and will determine the type of backend used.</remarks>
    public JournaledAttribute(string providerName, bool backgroundSave)
        => (ProviderName, BackgroundSave) = (providerName, backgroundSave);

    /// <summary>Log consistency provider used by the generated journaled grain.</summary>
    /// <remarks>Silo configuration for this provider name is required and will determine the type of backend used.</remarks>
    public string? ProviderName { get; }

    /// <summary>
    /// When <see langword="true"/>, generated journaled grains stop auto-awaiting <c>ConfirmEvents()</c>
    /// at command completion and instead rely on actor code to explicitly await it when desired.
    /// </summary>
    public bool BackgroundSave { get; }
}

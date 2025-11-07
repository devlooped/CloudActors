using System;
using System.ComponentModel;
using System.Linq;
using Devlooped.CloudActors;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans;
using Orleans.Runtime;

namespace Microsoft.Extensions.DependencyInjection;

[EditorBrowsable(EditorBrowsableState.Never)]
public static partial class CloudActorsExtensions
{
    /// <summary>
    /// Adds the <see cref="IActorBus"/> service and actor activation logic. 
    /// </summary>
    public static IServiceCollection AddCloudActors(this IServiceCollection services)
    {
        services.TryAddSingleton<IActorBus>(sp => new OrleansActorBus(sp.GetRequiredService<IGrainFactory>()));

        // Attempt to replace the OOB persistence so we don't require a parameterless constructor and always 
        // have actors initialized with a specific id from the grain.
        if (services.FirstOrDefault(d => d.ServiceType == typeof(IActorStateFactory)) is null &&
            services.FirstOrDefault(d => d.ServiceType == typeof(IPersistentStateFactory)) is { } descriptor)
        {
            services.Remove(descriptor);
            services.Replace(ServiceDescriptor.Describe(
              typeof(IPersistentStateFactory),
              s => new ActorStateFactory((IPersistentStateFactory)CreateInstance(s, descriptor)),
              descriptor.Lifetime
            ));
        }

        return services;
    }

    static object CreateInstance(IServiceProvider services, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is not null)
            return descriptor.ImplementationInstance;

        if (descriptor.ImplementationFactory is not null)
            return descriptor.ImplementationFactory(services);

        return ActivatorUtilities.GetServiceOrCreateInstance(services, descriptor.ImplementationType!);
    }
}
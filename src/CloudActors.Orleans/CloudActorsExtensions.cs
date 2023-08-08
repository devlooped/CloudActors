using System;
using System.ComponentModel;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans;
using Orleans.Hosting;
using Orleans.Runtime;

namespace Devlooped.CloudActors
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static partial class CloudActorsExtensions
    {
        /// <summary>
        /// Adds the <see cref="IActorBus"/> service and actor activation logic. 
        /// </summary>
        /// <remarks>
        /// It's not necessary to invoke this method if you already invoked <c>AddCloudActors</c> 
        /// on the <see cref="ISiloBuilder"/> when configuring Orleans.
        /// </remarks>
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
}
using System;
using System.ComponentModel;
using Devlooped.CloudActors;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Storage;

namespace Orleans.Hosting;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class StreamstoneSiloBuilderExtensions
{
    public static ISiloBuilder AddStreamstoneActorStorage(this ISiloBuilder builder)
        => builder.AddStreamstoneActorStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME);

    public static ISiloBuilder AddStreamstoneActorStorage(this ISiloBuilder builder, string name)
        => builder.ConfigureServices(services => services.AddStreamstoneActorStorage(name, null));

    public static ISiloBuilder AddStreamstoneActorStorage(this ISiloBuilder builder, string name, Action<StreamstoneOptions> configure)
        => builder.ConfigureServices(services => services.AddStreamstoneActorStorage(name, configure));

    public static ISiloBuilder AddStreamstoneActorStorage(this ISiloBuilder builder, Action<StreamstoneOptions> configure)
        => builder.ConfigureServices(services => services.AddStreamstoneActorStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configure));

    internal static IServiceCollection AddStreamstoneActorStorage(
        this IServiceCollection services,
        string name,
        Action<StreamstoneOptions>? configure = null)
    {
        if (configure is not null)
            services.AddOptions<StreamstoneOptions>(name).Configure(configure);

        if (string.Equals(name, ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, StringComparison.Ordinal))
        {
            services.TryAddSingleton(sp => sp.GetServiceByName<IGrainStorage>(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME));
        }

        return services.AddSingletonNamedService<IGrainStorage>(name, (sp, name) =>
        {
            var snapshot = sp.GetRequiredService<IOptionsMonitor<StreamstoneOptions>>();
            return new StreamstoneStorage(sp.GetRequiredService<CloudStorageAccount>(), snapshot.Get(name));
        });
    }
}

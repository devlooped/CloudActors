using System;
using System.Threading.Tasks;
using Devlooped;
using Devlooped.CloudActors;
using Microsoft.Extensions.DependencyInjection;
using TestDomain;

namespace Tests;

public class TestProducts : IAsyncDisposable
{
    public TestProducts() => CloudStorageAccount.DevelopmentStorageAccount
        .CreateCloudTableClient()
        .DeleteTable(nameof(Product));

    public async ValueTask DisposeAsync() => await CloudStorageAccount.DevelopmentStorageAccount
        .CreateCloudTableClient()
        .DeleteTableAsync(nameof(Product));

    [Fact]
    public async Task TypedIdRoundTrip()
    {
        using var cluster = ClusterFixture.CreateCluster();
        var bus = cluster.ServiceProvider.GetRequiredService<IActorBus>();

        var productId = new ProductId(Guid.NewGuid());

        // Use the generated typed-ID overloads
        await bus.ExecuteAsync(productId, new SetPrice(9.99m));

        var price = await bus.QueryAsync(productId, GetPrice.Default);
        Assert.Equal(9.99m, price);
    }

    [Fact]
    public async Task TypedIdPersistence()
    {
        var productId = new ProductId(Guid.NewGuid());

        using (var cluster = ClusterFixture.CreateCluster())
        {
            var bus = cluster.ServiceProvider.GetRequiredService<IActorBus>();
            await bus.ExecuteAsync(productId, new SetPrice(42.0m));
            Assert.Equal(42.0m, await bus.QueryAsync(productId, GetPrice.Default));
        }

        // Force grain re-activation from storage
        using (var cluster = ClusterFixture.CreateCluster())
        {
            var bus = cluster.ServiceProvider.GetRequiredService<IActorBus>();
            Assert.Equal(42.0m, await bus.QueryAsync(productId, GetPrice.Default));
        }
    }
}

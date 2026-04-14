using System;
using System.Threading.Tasks;
using Devlooped;
using Devlooped.CloudActors;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public partial record SetDescription(string Description) : IActorCommand;

public partial record GetDescription() : IActorQuery<string>
{
    public static GetDescription Default { get; } = new();
}

[Actor]
public partial class Catalog(long id)
{
    public long Id => id;

    public string Description { get; private set; } = "";

    public void Execute(SetDescription command) => Description = command.Description;

    public string Query(GetDescription _) => Description;
}

public class TestCatalogs : IAsyncDisposable
{
    public TestCatalogs() => CloudStorageAccount.DevelopmentStorageAccount
        .CreateCloudTableClient()
        .DeleteTable(nameof(Catalog));

    public async ValueTask DisposeAsync() => await CloudStorageAccount.DevelopmentStorageAccount
        .CreateCloudTableClient()
        .DeleteTableAsync(nameof(Catalog));

    [Fact]
    public async Task CatalogIdRoundTrip()
    {
        using var cluster = ClusterFixture.CreateCluster();
        var bus = cluster.ServiceProvider.GetRequiredService<IActorBus>();

        var catalogId = Catalog.NewId(1L);

        await bus.ExecuteAsync(catalogId, new SetDescription("Electronics"));

        var desc = await bus.QueryAsync(catalogId, GetDescription.Default);
        Assert.Equal("Electronics", desc);
    }

    [Fact]
    public async Task OrderAndCatalogAreIndependent()
    {
        using var cluster = ClusterFixture.CreateCluster();
        var bus = cluster.ServiceProvider.GetRequiredService<IActorBus>();

        // Both use long(1) as the underlying ID, but the generated wrapper types
        // (Order.OrderId vs Catalog.CatalogId) route to different actors.
        var orderId = Order.NewId(1L);
        var catalogId = Catalog.NewId(1L);

        await bus.ExecuteAsync(orderId, new SetTotal(19.99m));
        await bus.ExecuteAsync(catalogId, new SetDescription("Books"));

        Assert.Equal(19.99m, await bus.QueryAsync(orderId, GetTotal.Default));
        Assert.Equal("Books", await bus.QueryAsync(catalogId, GetDescription.Default));
    }
}

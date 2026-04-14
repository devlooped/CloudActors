using System;
using System.Threading.Tasks;
using Devlooped;
using Devlooped.CloudActors;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public partial record SetTotal(decimal Total) : IActorCommand;

public partial record GetTotal() : IActorQuery<decimal>
{
    public static GetTotal Default { get; } = new();
}

[Actor]
public partial class Order(long id)
{
    public long Id => id;

    public decimal Total { get; private set; }

    public void Execute(SetTotal command) => Total = command.Total;

    public decimal Query(GetTotal _) => Total;
}

public class TestOrders : IAsyncDisposable
{
    public TestOrders() => CloudStorageAccount.DevelopmentStorageAccount
        .CreateCloudTableClient()
        .DeleteTable(nameof(Order));

    public async ValueTask DisposeAsync() => await CloudStorageAccount.DevelopmentStorageAccount
        .CreateCloudTableClient()
        .DeleteTableAsync(nameof(Order));

    [Fact]
    public async Task LongIdRoundTrip()
    {
        using var cluster = ClusterFixture.CreateCluster();
        var bus = cluster.ServiceProvider.GetRequiredService<IActorBus>();

        var orderId = Order.NewId(42L);

        await bus.ExecuteAsync(orderId, new SetTotal(99.99m));

        var total = await bus.QueryAsync(orderId, GetTotal.Default);
        Assert.Equal(99.99m, total);
    }

    [Fact]
    public async Task LongIdPersistence()
    {
        var orderId = Order.NewId(100L);

        using (var cluster = ClusterFixture.CreateCluster())
        {
            var bus = cluster.ServiceProvider.GetRequiredService<IActorBus>();
            await bus.ExecuteAsync(orderId, new SetTotal(49.99m));
            Assert.Equal(49.99m, await bus.QueryAsync(orderId, GetTotal.Default));
        }

        using (var cluster = ClusterFixture.CreateCluster())
        {
            var bus = cluster.ServiceProvider.GetRequiredService<IActorBus>();
            Assert.Equal(49.99m, await bus.QueryAsync(orderId, GetTotal.Default));
        }
    }
}

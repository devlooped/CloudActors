using System;
using System.Threading.Tasks;
using Devlooped;
using Devlooped.CloudActors;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public partial record SetLabel(string Label) : IActorCommand;

public partial record GetLabel() : IActorQuery<string>
{
    public static GetLabel Default { get; } = new();
}

[Actor]
public partial class Item(Guid id)
{
    public Guid Id => id;

    public string Label { get; private set; } = "";

    public void Execute(SetLabel command) => Label = command.Label;

    public string Query(GetLabel _) => Label;
}

public class TestItems : IAsyncDisposable
{
    public TestItems() => CloudStorageAccount.DevelopmentStorageAccount
        .CreateCloudTableClient()
        .DeleteTable(nameof(Item));

    public async ValueTask DisposeAsync() => await CloudStorageAccount.DevelopmentStorageAccount
        .CreateCloudTableClient()
        .DeleteTableAsync(nameof(Item));

    [Fact]
    public async Task GuidIdRoundTrip()
    {
        using var cluster = ClusterFixture.CreateCluster();
        var bus = cluster.ServiceProvider.GetRequiredService<IActorBus>();

        var itemId = Item.NewId();

        await bus.ExecuteAsync(itemId, new SetLabel("Widget"));

        var label = await bus.QueryAsync(itemId, GetLabel.Default);
        Assert.Equal("Widget", label);
    }

    [Fact]
    public async Task GuidIdPersistence()
    {
        var itemId = Item.NewId();

        using (var cluster = ClusterFixture.CreateCluster())
        {
            var bus = cluster.ServiceProvider.GetRequiredService<IActorBus>();
            await bus.ExecuteAsync(itemId, new SetLabel("Gadget"));
            Assert.Equal("Gadget", await bus.QueryAsync(itemId, GetLabel.Default));
        }

        using (var cluster = ClusterFixture.CreateCluster())
        {
            var bus = cluster.ServiceProvider.GetRequiredService<IActorBus>();
            Assert.Equal("Gadget", await bus.QueryAsync(itemId, GetLabel.Default));
        }
    }
}

using System;
using System.Threading.Tasks;
using Devlooped;
using Devlooped.CloudActors;
using Microsoft.Extensions.DependencyInjection;
using TestDomain;

namespace Tests;

public partial record SetName(string Name) : IActorCommand;

public partial record GetName() : IActorQuery<string>
{
    public static GetName Default { get; } = new();
}

[Actor]
public partial class User(UserId id)
{
    public UserId Id => id;

    public string Name { get; private set; } = "";

    public void Execute(SetName command) => Name = command.Name;

    public string Query(GetName _) => Name;
}

public class TestUsers : IAsyncDisposable
{
    public TestUsers() => CloudStorageAccount.DevelopmentStorageAccount
        .CreateCloudTableClient()
        .DeleteTable(nameof(User));

    public async ValueTask DisposeAsync() => await CloudStorageAccount.DevelopmentStorageAccount
        .CreateCloudTableClient()
        .DeleteTableAsync(nameof(User));

    [Fact]
    public async Task TypedIdRoundTrip()
    {
        using var cluster = ClusterFixture.CreateCluster();
        var bus = cluster.ServiceProvider.GetRequiredService<IActorBus>();

        var userId = new UserId(42L);

        // Use the generated typed-ID overloads
        await bus.ExecuteAsync(userId, new SetName("Alice"));

        var name = await bus.QueryAsync(userId, GetName.Default);
        Assert.Equal("Alice", name);
    }

    [Fact]
    public async Task TypedIdPersistence()
    {
        var userId = new UserId(123L);

        using (var cluster = ClusterFixture.CreateCluster())
        {
            var bus = cluster.ServiceProvider.GetRequiredService<IActorBus>();
            await bus.ExecuteAsync(userId, new SetName("Bob"));
            Assert.Equal("Bob", await bus.QueryAsync(userId, GetName.Default));
        }

        // Force grain re-activation from storage
        using (var cluster = ClusterFixture.CreateCluster())
        {
            var bus = cluster.ServiceProvider.GetRequiredService<IActorBus>();
            Assert.Equal("Bob", await bus.QueryAsync(userId, GetName.Default));
        }
    }
}

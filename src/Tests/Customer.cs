using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Devlooped;
using Devlooped.CloudActors;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Xunit.Abstractions;

namespace Tests;

[Collection(ClusterCollection.Name)]
public class TestCustomers(ITestOutputHelper output, ClusterFixture fixture)
{
    [Fact]
    public async Task HostedGrain()
    {
        await CloudStorageAccount.DevelopmentStorageAccount
            .CreateCloudTableClient()
            .DeleteTableAsync("customer");

        var bus = fixture.Cluster.ServiceProvider.GetRequiredService<IActorBus>();

        var address = new Address("One Redmond Way", "Redmond", "WA", "98052");

        await bus.ExecuteAsync("customer/asdf", new SetAddress(address));

        var saved = await bus.QueryAsync("customer/asdf", new GetAddress());

        Assert.NotNull(address);
        Assert.Equal(address, saved);
    }
}

public record Address(string Street, string City, string State, string Zip);

public partial record SetAddress(Address Address) : IActorCommand;

public partial record GetAddress() : IActorQuery<Address>;

[Actor]
public partial class Customer
{
    public Customer() : this("") { }

    public Customer(string id) => Id = id;

    public string Id { get; }

    public Address? Address { get; private set; }

    // Showcases plain POCO sync operation
    public void Execute(SetAddress command) => Address = command.Address;

    // Showcases a query that doesn't change state, which becomes a [ReadOnly] grain operation.
    public Address? Query(GetAddress _) => Address;
}


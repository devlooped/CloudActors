using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Devlooped.CloudActors;
using Microsoft.Azure.Cosmos.Table;
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
            .GetTableReference("customer")
            .DeleteIfExistsAsync();

        IActorBus bus = new OrleansActorBus(fixture.Cluster.GrainFactory);
        var address = new Address("One Redmond Way", "Redmond", "WA", "98052");

        await bus.ExecuteAsync("customer/asdf", new SetAddress(address));

        var saved = await bus.QueryAsync("customer/asdf", new GetAddress());

        Assert.NotNull(address);
        Assert.Equal(address, saved);
    }
}

[GenerateSerializer]
public record Address(string Street, string City, string State, string Zip);

[GenerateSerializer]
public partial record SetAddress(Address Address) : IActorCommand;

[GenerateSerializer]
public partial record GetAddress(): IActorQuery<Address>;

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


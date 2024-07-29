using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Devlooped;
using Devlooped.CloudActors;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using Newtonsoft.Json;
using Orleans;
using Orleans.Configuration;
using Orleans.Core;
using Orleans.Hosting;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Runtime.Development;
using Orleans.Storage;
using TestDomain;
using Xunit.Abstractions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Tests;

public class TestAccounts : IAsyncDisposable
{
    public TestAccounts() => CloudStorageAccount.DevelopmentStorageAccount
        .CreateCloudTableClient()
        .DeleteTable(nameof(Account));

    public async ValueTask DisposeAsync() => await CloudStorageAccount.DevelopmentStorageAccount
        .CreateCloudTableClient()
        .DeleteTableAsync(nameof(Account));

    [Fact]
    public async Task HostedGrain()
    {
        using (var cluster = ClusterFixture.CreateCluster())
        {
            var bus = cluster.ServiceProvider.GetRequiredService<IActorBus>();

            await bus.ExecuteAsync("account/1", new Deposit(100));
            await bus.ExecuteAsync("account/1", new Withdraw(50));

            var balance = await bus.QueryAsync("account/1", new GetBalance());
            Assert.Equal(50, balance);

            Assert.Equal(50, await bus.ExecuteAsync("account/1", new Close(CloseReason.Customer)));
            Assert.Equal(0, await bus.QueryAsync("account/1", new GetBalance()));
        }

        // Force re-activation of grain.
        using (var cluster = ClusterFixture.CreateCluster())
        {
            var bus = cluster.ServiceProvider.GetRequiredService<IActorBus>();
            Assert.Equal(0, await bus.QueryAsync("account/1", new GetBalance()));
        }
    }

    [Fact]
    public async Task WebAppHosting()
    {
        var siloPort = 11111;
        int gatewayPort = 30000;
        var siloAddress = IPAddress.Loopback;

        var host = Host
            .CreateDefaultBuilder()
            .UseOrleans((ctx, builder) => builder
                .Configure<ClusterOptions>(options => options.ClusterId = "TEST")
                .UseDevelopmentClustering(options => options.PrimarySiloEndpoint = new IPEndPoint(siloAddress, siloPort))
                .ConfigureEndpoints(siloAddress, siloPort, gatewayPort)
                .AddStreamstoneActorStorage(opt => opt.AutoSnapshot = true)
            )
            .ConfigureServices(services =>
            {
                services.AddSingleton(CloudStorageAccount.DevelopmentStorageAccount);
                services.AddCloudActors();
            }).Build();

        //builder.Host.UseOrleans(silo =>
        //{
        //    silo.UseDevelopmentClustering(new IPEndPoint(IPAddress.Loopback, 1234))
        //    .ConfigureEndpoints(IPAddress.Loopback, 1234, 30000);
        //    silo.AddCloudActors();
        //});

        host.Start();

        //var app = builder.Build();
        //var task = Task.Run(app.Run);

        var bus = host.Services.GetRequiredService<IActorBus>();

        await bus.ExecuteAsync("account/1", new Deposit(100));
        await bus.ExecuteAsync("account/1", new Withdraw(50));

        var balance = await bus.QueryAsync("account/1", new GetBalance());
        Assert.Equal(50, balance);

        Assert.Equal(50, await bus.ExecuteAsync("account/1", new Close()));
        Assert.Equal(0, await bus.QueryAsync("account/1", new GetBalance()));

        await host.StopAsync();
    }
}
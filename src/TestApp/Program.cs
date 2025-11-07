using System.Net;
using Devlooped;
using Devlooped.CloudActors;
using Microsoft.AspNetCore.Mvc;
using Orleans.Configuration;
using TestDomain;

var siloPort = 11111;
var gatewayPort = 30000;
var siloAddress = IPAddress.Loopback;

var builder = WebApplication.CreateBuilder(args);

builder.UseOrleans(builder => builder
        .Configure<ClusterOptions>(options => options.ClusterId = "TestApp")
        .UseDevelopmentClustering(options => options.PrimarySiloEndpoint = new IPEndPoint(siloAddress, siloPort))
        .ConfigureEndpoints(siloAddress, siloPort, gatewayPort)
        .AddStreamstoneActorStorage(opt => opt.AutoSnapshot = true)
    );

builder.Services.AddSingleton(CloudStorageAccount.DevelopmentStorageAccount);
builder.Services.AddCloudActors();

var app = builder.Build();

app.MapPost("/account", async (IActorBus bus) =>
{
    var id = Guid.CreateVersion7().ToString("N");
    return Results.Created($"/account/{id}", new { id });
});

app.MapGet("/account/{id}", async (string id, IActorBus bus) =>
{
    var balance = await bus.QueryAsync($"account/{id.Trim('"')}", GetBalance.Default);
    return Results.Ok(balance);
});

app.MapMethods("/account/{id}", ["PUT", "POST"], async (string id, [FromBody] decimal amount, IActorBus bus) =>
{
    id = id.Trim('"');
    //var amount = decimal.Parse(text);
    if (amount > 0)
        await bus.ExecuteAsync($"account/{id}", new Deposit(amount));
    else if (amount < 0)
        await bus.ExecuteAsync($"account/{id}", new Withdraw(-amount));

    var balance = await bus.QueryAsync($"account/{id}", GetBalance.Default);
    return Results.Ok(balance);
});

app.MapDelete("/account/{id}", async (string id, IActorBus bus) =>
{
    var balance = await bus.ExecuteAsync($"account/{id.Trim('"')}", new Close(CloseReason.Customer));
    return Results.Ok(balance);
});

app.Run();

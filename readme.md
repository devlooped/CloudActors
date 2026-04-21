# Cloud Native Actors

<p align="center">
  <image src="https://raw.githubusercontent.com/devlooped/CloudActors/main/assets/img/banner.png" alt="Orleans logo" width="320px">
</p>

An opinionated, simplified and uniform Cloud Native actors' library that integrates with Microsoft Orleans.

[![Version](https://img.shields.io/nuget/v/Devlooped.CloudActors.svg?color=royalblue)](https://www.nuget.org/packages/Devlooped.CloudActors) 
[![Downloads](https://img.shields.io/nuget/dt/Devlooped.CloudActors.svg?color=darkmagenta)](https://www.nuget.org/packages/Devlooped.CloudActors) 
[![EULA](https://img.shields.io/badge/EULA-OSMF-blue?labelColor=black&color=C9FF30)](osmfeula.txt)
[![OSS](https://img.shields.io/github/license/devlooped/oss.svg?color=blue)](license.txt) 

## Motivation

Watch the [Orleans Virtual Meetup 7](https://www.youtube.com/watch?v=FKL-PS8Q9ac) where Yevhen 
(of [Streamstone](https://github.com/yevhen/Streamstone) fame) makes the case for using message
passing style with actors instead of the more common RPC style offered by Orleans.

<!-- include https://github.com/devlooped/.github/raw/main/osmf.md -->
## Open Source Maintenance Fee

To ensure the long-term sustainability of this project, users of this package who generate 
revenue must pay an [Open Source Maintenance Fee](https://opensourcemaintenancefee.org). 
While the source code is freely available under the terms of the [License](license.txt), 
this package and other aspects of the project require [adherence to the Maintenance Fee](osmfeula.txt).

To pay the Maintenance Fee, [become a Sponsor](https://github.com/sponsors/devlooped) at the proper 
OSMF tier. A single fee covers all of [Devlooped packages](https://www.nuget.org/profiles/Devlooped).

<!-- https://github.com/devlooped/.github/raw/main/osmf.md -->

<!-- #content -->
## Overview

Rather than the RPC-style programming offered (and encouraged) out of the 
box by Orleans, Cloud Native Actors offers a message-passing style of programming 
with a uniform API to access actors: Execute and Query. 

These uniform operations receive a message (a.k.a. command or query) and 
optionally return a result. Consumers always use the same API to invoke 
operations on actors, and the combination of the actor id and the message 
constitute enough information to route the message to the right actor.

Actors can be implemented as plain CLR objects, with no need to inherit 
from any base class or implement any interface. The Orleans plumbing of 
grains and their activation is completely hidden from the developer.

## Features

Rather than relying on `dynamic` dispatch, this implementation relies heavily on source generators 
to provide strong-typed routing of messages, while preserving a flexible mechanism for implementors.

In addition, this library makes the grains completely transparent to the developer. They don't even 
need to take a dependency on Orleans. In other words: the developer writes his business logic as 
a plain CLR object (POCO).

`[Actor]` keeps the default snapshot-backed grain generation. Event-sourced actors can additionally opt into generated journaled grains with `[Journaled]`, still without inheriting from Orleans types.

The central abstraction of the library is the actor bus:

```csharp
public interface IActorBus
{
    Task ExecuteAsync(string id, IActorCommand command);
    Task<TResult> ExecuteAsync<TResult>(string id, IActorCommand<TResult> command);
    Task<TResult> QueryAsync<TResult>(string id, IActorQuery<TResult> query);
}
```

Actors receive messages to process, which are typically plain records such as:

```csharp
public partial record Deposit(decimal Amount) : IActorCommand;  // 👈 marker interface for void commands

public partial record Withdraw(decimal Amount) : IActorCommand;

public partial record Close(CloseReason Reason = CloseReason.Customer) : IActorCommand<decimal>; // 👈 marker interface for value-returning commands

public enum CloseReason
{
    Customer,
    Fraud,
    Other
}

public partial record GetBalance() : IActorQuery<decimal>; // 👈 marker interface for queries (a.k.a. readonly methods)
```

We can see that the only thing that distinguishes a regular Orleans parameter 
from an actor message, is that it implements the `IActorCommand` or `IActorQuery` 
interface. You can see the three types of messages supported by the library:

* `IActorCommand` - a message that is sent to an actor to be processed, but does not return a result.
* `IActorCommand<TResult>` - a message that is sent to an actor to be processed, and returns a result.
* `IActorQuery<TResult>` - a message that is sent to an actor to be processed, and returns a result. 
  It differs from the previous type in that it is a read-only operation, meaning it does not mutate 
  the state of the actor. This causes a [Readonly method](https://learn.microsoft.com/en-us/dotnet/orleans/grains/request-scheduling#readonly-methods) 
  invocation on the grain.

The actor, for its part, only needs the `[Actor]` attribute to be recognized as such:

```csharp
[Actor]
public partial class Account(string id)    // 👈 no need for parameterless constructor or inheriting anything by default
{
    public string Id { get; } = id;
    public decimal Balance { get; private set; }
    public bool IsClosed { get; private set; }
    public CloseReason Reason { get; private set; }

    //public void Execute(Deposit command)      // 👈 methods can be overloads of message types
    //{
    //    // validate command
    //    // decrease balance
    //}

    // Showcases that operations can have a name that's not Execute
    public Task DepositAsync(Deposit command)   // 👈 but can also use any name you like
    {
        // validate command
        Balance += command.Amount;
        return Task.CompletedTask;
    }

    // Showcases that operations don't have to be async
    public void Execute(Withdraw command)       // 👈 methods can be sync too
    {
        // validate command
        Balance -= command.Amount;
    }

    // Showcases value-returning operation with custom name.
    // In this case, closing the account returns the final balance.
    // As above, this can be async or not.
    public decimal Close(Close command)
    {
        var balance = Balance;
        Balance = 0;
        IsClosed = true;
        Reason = command.Reason;
        return balance;
    }

    // Showcases a query that doesn't change state
    public decimal Query(GetBalance _) => Balance;  // 👈 becomes [ReadOnly] grain operation
}
```

> NOTE: no attributes are needed anywhere for state persistence — only the `[Actor]` attribute 
> on the class itself is required. The source generator automatically includes in persisted state:
> all properties that have a setter (regardless of accessibility — `public`, `private`, etc.), and 
> all non-`const`, non-`static`, non-`readonly` instance fields. Get-only properties and `readonly` 
> fields are excluded, as they are expected to be initialized via the constructor or derived from 
> other state.

On the hosting side, an `AddCloudActors` extension method is provided to register the 
automatically generated grains to route invocations to the actors:

```csharp
var builder = WebApplication.CreateSlimBuilder(args);

builder.Host.UseOrleans(silo =>
{
    silo.UseLocalhostClustering();
});

// 👇 registers generated grains, actor bus and activation features
builder.Services.AddCloudActors(); 
```

## State Deserialization

The above `Account` class only provides a single constructor receiving the account 
identifier. After various operations are performed on it, however, the state will 
be changed via private property setters (or direct field mutation). When you annotate 
a class with the `[Actor]` attribute, a source generator will create an inner class to 
hold all state properties (and fields), and implement (explicitly) an `IActor<TState>` 
interface to allow getting/setting the instance state.

This provides seamless integration with Orleans' recommended `IPersistentState<T>` 
injection mechanism used by the generated grain.

The generator produces a nested `ActorState` record for the above `Account` actor, 
capturing its mutable state as an Orleans-serializable snapshot:

```csharp
[GeneratedCode("Devlooped.CloudActors")]
[GenerateSerializer]
public partial class ActorState : IActorState<Account>
{
    [Id(0)] public decimal Balance;
    [Id(1)] public bool IsClosed;
    [Id(2)] public CloseReason Reason;
}
```

This is a sort of typed [Memento pattern](https://grokipedia.com/page/Memento_pattern) which allows 
the Orleans state persistence mechanisms to read and write the actor state without requiring 
any additional code from the developer.

> [!NOTE]
> The generated `ActorState` is always in sync with the actor's mutable members because it is 
> regenerated at compile time. State is read from storage on activation and written after each 
> successful command — with an automatic rollback (re-read) if the write fails.

## Event Sourcing

Quite a natural extension of the message-passing style of programming for these actors, 
is to go full event sourcing. The library provides an interface `IEventSourced` for that:

```csharp
public interface IEventSourced
{
    IReadOnlyList<object> Events { get; }
    void AcceptEvents();
    void LoadEvents(IEnumerable<object> history);
}
```

The sample [Streamstone](https://github.com/yevhen/Streamstone)-based grain storage will 
invoke `LoadEvents` with the events from the stream (if found), and `AcceptEvents` will be 
invoked after the grain is saved, so it can clear the events list.

Optimistic concurrency is implemented by exposing the stream version as the `IGrainState.ETag` 
and parsing it when persisting to ensure consistency.

Users are free to implement this interface in any way they deem fit, but the library provides 
a default implementation if the interface is inherited but not implemented. The generated 
implementation provides a `Raise<T>(@event)` method for the actor's methods to raise events, 
and invokes provided `Apply(@event)` methods to apply the events to mutate state. The generator 
assumes this convention, using the single parameter to every `Apply` method on the actor as 
the switch to route events (either when raised or loaded from storage).

For example, if the above `Account` actor was converted to an event-sourced actor, it would 
look like this:

```csharp
[Actor]
public partial class Account : IEventSourced  // 👈 interface is *not* implemented by user!
{
    public Account(string id) => Id = id;

    public string Id { get; }
    public decimal Balance { get; private set; }
    public bool IsClosed { get; private set; }

    public void Execute(Deposit command)
    {
        if (IsClosed)
            throw new InvalidOperationException("Account is closed");

        // 👇 Raise<T> is generated when IEventSourced is inherited
        Raise(new Deposited(command.Amount));
    }

    public void Execute(Withdraw command)
    {
        if (IsClosed)
            throw new InvalidOperationException("Account is closed");
        if (command.Amount > Balance)
            throw new InvalidOperationException("Insufficient funds.");

        Raise(new Withdrawn(command.Amount));
    }

    public decimal Execute(Close command)
    {
        if (IsClosed)
            throw new InvalidOperationException("Account is closed");

        var balance = Balance;
        Raise(new Closed(Balance, command.Reason));
        return balance;
    }

    public decimal Query(GetBalance _) => Balance;

    // 👇 generated generic Apply(object) dispatches to each based on event type with no reflection

    partial void Apply(Deposited @event) => Balance += @event.Amount;

    partial void Apply(Withdrawn @event) => Balance -= @event.Amount;

    partial void Apply(Closed @event)
    {
        Balance = 0;
        IsClosed = true;
        Reason = @event.Reason;
    }
}
```

> [!TIP]
> By generating the partial `Apply` methods, the generator allows users to implement only the 
> event types they care about, without needing to provide an empty implementation for the rest.

When `IEventSourced` is inherited without being implemented, the generator provides the full
wiring: `Raise<T>(event)` / `Raise<T>()` methods that apply the event and record it in the 
pending events list, a type-switched `Apply(object)` dispatcher, and `partial void` declarations
for each event type raised. There is also an optional hook for post-raise callbacks:

```csharp
// Invoked after every Raise<T>(event) call — implement to react to raised events.
partial void OnRaised<T>(T @event) where T : notnull;
```

> [!NOTE]
> Note how there's no dynamic dispatch here 💯.

## Journaled Actors

When an event-sourced actor needs Orleans journaling semantics, add `[Journaled]` alongside `[Actor]`:

```csharp
[Actor]
[Journaled]
public partial class Account(string id) : IEventSourced
{
    public string Id { get; } = id;
    public decimal Balance { get; private set; }

    public void Execute(Deposit command) => Raise(new Deposited(command.Amount));

    partial void Apply(Deposited @event) => Balance += @event.Amount;
}
```

CloudActors keeps the actor itself as a POCO and generates a `JournaledGrain<Account.ActorState, object>` wrapper behind the scenes. The generated nested `ActorState` becomes the Orleans `TView`, while commands and queries still execute through transient instances of the actor class.

`[Journaled]` defaults to whichever backend is configured for JournaledGrainOptions.DefaultLogConsistencyProvider, such as: 

```csharp
silo.Configure<JournaledGrainOptions>(options =>
{
    options.DefaultLogConsistencyProvider = "StateStorage";     // or "LogStorage", "CustomStorage", or your own name
});
```

When the host references `Devlooped.CloudActors.Streamstone`, a custom storage implementation is provided automatically for 
the grains, and calling `AddStreamstoneActorStorageAsDefault()` or `AddStreamstoneActorStorage("name")` will connect that 
custom log-consistency provider accordingly in Orleans. Orleans-native alternatives are still available in this case by 
passing custom provider names to the attribute (i.e. `[Journaled("StateStorage")]` or `[Journaled("LogStorage")]`) and 
registering them as usual in the silo configuration:

```csharp
silo.AddStateStorageLogConsistencyProvider("StateStorage");
silo.AddLogStorageLogConsistencyProvider("LogStorage");
```

An important corollary of this project is that the design of a library and particularly 
its implementation details, will vary greatly if it can assume source generators will 
play a role in its consumption. In this particular case, many design decisions 
were different initially before I had the generators in place, and the result afterwards 
was a simplification in many aspects, with less base types in the main library/interfaces 
project, and more incremental behavior added as users opt-in to certain features.

## Typed Actor IDs

All actors get a strongly-typed IDs for free — CloudActors emits proper typed overloads so 
you never have to use raw `string` composite keys for bus calls, which prevents accidentally 
passing the wrong ID format (e.g. `"account/1"` vs. `"1"`).

### String IDs

When the actor's first constructor parameter is `string`, the generator produces a `{Actor}Id` 
wrapper struct so callers use type-safe IDs against the bus, while getting useful completion from 
overloads exposing the applicable messages the actor can handle:

```csharp
[Actor]
public partial class Account(string id)
{
    public string Id { get; } = id;
    // ...
}

// Generated:
//   public readonly record struct AccountId(string Id);
//   public static AccountId NewId(string id) => new(id);
```

```csharp
var id = Account.NewId("1");

await bus.ExecuteAsync(id, new Deposit(100));
var balance = await bus.QueryAsync(id, GetBalance.Default);
```

### Primitive IDs

When the first constructor parameter is a primitive BCL value type (e.g. `long`, `Guid`), 
the generator produces a nested `{Actor}Id` wrapper struct and a `NewId` factory method:

```csharp
[Actor]
public partial class Order(long id)
{
    public long Id => id;
    // ...
}

// Generated:
//   public readonly record struct OrderId(long Id);
//   public static OrderId NewId(long id) => new(id);
```

For `Guid` IDs a parameterless `NewId()` is also generated, using `Guid.CreateVersion7()` 
on .NET 9+ or `Guid.NewGuid()` on earlier runtimes.

### Structured IDs

Any strongly-typed ID library that generates `IParsable<TSelf>` and `IFormattable` on the ID 
type works out of the box. The most popular choices include:

- [StructId](https://www.nuget.org/packages/StructId) — single-line `IStructId<T>` declaration, source-generated
- [StronglyTypedId](https://www.nuget.org/packages/StronglyTypedId) — attribute-driven, supports multiple backing types
- [Vogen](https://www.nuget.org/packages/Vogen) — value-object generator with validation support

Cloud Native Actors detects these types automatically: if the actor's first constructor parameter 
implements `IParsable<T>`, it is treated as the typed ID and the generator produces typed 
`IActorBus` overloads for it — no extra configuration required.

Here is an example using [StructId](https://www.nuget.org/packages/StructId):

```csharp
// Just declare the struct — StructId generates all the boilerplate
public readonly partial record struct ProductId : IStructId<Guid>;

[Actor]
public partial class Product(ProductId id)
{
    public ProductId Id => id;

    public decimal Price { get; private set; }

    public void Execute(SetPrice command) => Price = command.Price;

    public decimal Query(GetPrice _) => Price;
}
```

```csharp
var id = new ProductId(Guid.CreateVersion7()); // or ProductId.New()

await bus.ExecuteAsync(id, new SetPrice(9.99m));
var price = await bus.QueryAsync(id, new GetPrice());
```

### Typed Bus Overloads

For every actor, the generator produces typed `IActorBus` extension overloads — one per 
handled message — so you can never accidentally pass the wrong message to the wrong actor:

```csharp
// Only valid messages for Order are available via OrderId:
await bus.ExecuteAsync(new OrderId(42), new PlaceOrder(...));

// Compile error: Deposit is not a valid message for Order
await bus.ExecuteAsync(new OrderId(42), new Deposit(100)); // ❌
```

The ID is always routed as `"{actortype}/{id}"` (e.g. `"order/42"`, `"product/..."`) 
as expected by Orleans, but the typed overloads shield you from that detail and provide 
type safety.

## Telemetry and Monitoring

The core implementation of the `IActorBus` is instrumented with `ActivitySource` and 
`Metric`, providing out of the box support for [Open Telemetry](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs)-based monitoring, as well 
as via [dotnet trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace) 
and [dotnet counters](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters).

To export telemetry using [Open Telemetry](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs), 
for example:

```csharp
using var tracer = Sdk
    .CreateTracerProviderBuilder()
    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("ConsoleApp"))
    .AddSource(source.Name) // other app sources
    .AddSource("CloudActors")
    .AddConsoleExporter()
    .AddZipkinExporter()
    .AddAzureMonitorTraceExporter(o => o.ConnectionString = config["AppInsights"])
    .Build();
```

Collecting traces via [dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace):

```shell
dotnet trace collect --name [PROCESS_NAME] --providers="Microsoft-Diagnostics-DiagnosticSource:::FilterAndPayloadSpecs=[AS]CloudActors,System.Diagnostics.Metrics:::Metrics=CloudActors"
```

Monitoring metrics via [dotnet-counters](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters):

```shell
dotnet counters monitor --process-id [PROCESS_ID] --counters CloudActors
```

## How it works

The library uses source generators to generate the grain classes. It's easy to inspect the 
generated code by setting the `EmitCompilerGeneratedFiles` property to `true` in the project 
and inspecting the `obj` folder.

For each `[Actor]` class the generator produces a partial `{Name}Grain : Grain` class that:

- Injects `IPersistentState<ActorState>` and reads it on activation
- Routes incoming `IActorCommand` / `IActorCommand<TResult>` / `IActorQuery<TResult>` to the 
  exact method you defined on your actor — preserving your method name and sync/async signature
- After every command writes the new state to storage; on failure, rolls back by re-reading
- Marks `QueryAsync` with `[ReadOnly]` so Orleans handles it as a concurrent read

Because the grain is `partial`, you can extend it with your own members in a separate file 
if needed.

> [!NOTE]
> Note how there's no dynamic dispatch 💯. Message routing is a compile-time `switch` on the 
> concrete message type, generated by the source generator directly from your actor's methods.

Since source generators [can't depend on other generated code](https://github.com/dotnet/roslyn/issues/57239),
grain types are registered with Orleans through assembly-level attributes (`ApplicationPartAttribute` 
and `GenerateCodeForDeclaringAssembly`) emitted by the generator into the silo/host project — 
no manual grain type registration is needed.

The `services.AddCloudActors()` call (generated as an extension on `IServiceCollection`) registers:

- `IActorBus` → `OrleansActorBus`  
- `IActorIdFactory` → a compile-time factory that parses typed actor IDs without reflection  
- Replaces `IPersistentStateFactory` with a wrapper that passes the typed ID to the actor constructor  

Finally, in order to improve discoverability for consumers of the `IActorBus` interface,
extension method overloads are generated that surface the available actor messages 
as non-generic overloads, such as:

![execute overloads](https://raw.githubusercontent.com/devlooped/CloudActors/main/assets/img/command-overloads.png?raw=true)

![query overloads](https://raw.githubusercontent.com/devlooped/CloudActors/main/assets/img/query-overloads.png?raw=true)

<!-- #content -->

<!-- #sponsors -->
<!-- include https://github.com/devlooped/sponsors/raw/main/footer.md -->
# Sponsors 

<!-- sponsors.md -->
[![Clarius Org](https://avatars.githubusercontent.com/u/71888636?v=4&s=39 "Clarius Org")](https://github.com/clarius)
[![MFB Technologies, Inc.](https://avatars.githubusercontent.com/u/87181630?v=4&s=39 "MFB Technologies, Inc.")](https://github.com/MFB-Technologies-Inc)
[![Khamza Davletov](https://avatars.githubusercontent.com/u/13615108?u=11b0038e255cdf9d1940fbb9ae9d1d57115697ab&v=4&s=39 "Khamza Davletov")](https://github.com/khamza85)
[![SandRock](https://avatars.githubusercontent.com/u/321868?u=99e50a714276c43ae820632f1da88cb71632ec97&v=4&s=39 "SandRock")](https://github.com/sandrock)
[![DRIVE.NET, Inc.](https://avatars.githubusercontent.com/u/15047123?v=4&s=39 "DRIVE.NET, Inc.")](https://github.com/drivenet)
[![Keith Pickford](https://avatars.githubusercontent.com/u/16598898?u=64416b80caf7092a885f60bb31612270bffc9598&v=4&s=39 "Keith Pickford")](https://github.com/Keflon)
[![Thomas Bolon](https://avatars.githubusercontent.com/u/127185?u=7f50babfc888675e37feb80851a4e9708f573386&v=4&s=39 "Thomas Bolon")](https://github.com/tbolon)
[![Kori Francis](https://avatars.githubusercontent.com/u/67574?u=3991fb983e1c399edf39aebc00a9f9cd425703bd&v=4&s=39 "Kori Francis")](https://github.com/kfrancis)
[![Reuben Swartz](https://avatars.githubusercontent.com/u/724704?u=2076fe336f9f6ad678009f1595cbea434b0c5a41&v=4&s=39 "Reuben Swartz")](https://github.com/rbnswartz)
[![Jacob Foshee](https://avatars.githubusercontent.com/u/480334?v=4&s=39 "Jacob Foshee")](https://github.com/jfoshee)
[![](https://avatars.githubusercontent.com/u/33566379?u=bf62e2b46435a267fa246a64537870fd2449410f&v=4&s=39 "")](https://github.com/Mrxx99)
[![Eric Johnson](https://avatars.githubusercontent.com/u/26369281?u=41b560c2bc493149b32d384b960e0948c78767ab&v=4&s=39 "Eric Johnson")](https://github.com/eajhnsn1)
[![Jonathan ](https://avatars.githubusercontent.com/u/5510103?u=98dcfbef3f32de629d30f1f418a095bf09e14891&v=4&s=39 "Jonathan ")](https://github.com/Jonathan-Hickey)
[![Ken Bonny](https://avatars.githubusercontent.com/u/6417376?u=569af445b6f387917029ffb5129e9cf9f6f68421&v=4&s=39 "Ken Bonny")](https://github.com/KenBonny)
[![Simon Cropp](https://avatars.githubusercontent.com/u/122666?v=4&s=39 "Simon Cropp")](https://github.com/SimonCropp)
[![agileworks-eu](https://avatars.githubusercontent.com/u/5989304?v=4&s=39 "agileworks-eu")](https://github.com/agileworks-eu)
[![Zheyu Shen](https://avatars.githubusercontent.com/u/4067473?v=4&s=39 "Zheyu Shen")](https://github.com/arsdragonfly)
[![Vezel](https://avatars.githubusercontent.com/u/87844133?v=4&s=39 "Vezel")](https://github.com/vezel-dev)
[![ChilliCream](https://avatars.githubusercontent.com/u/16239022?v=4&s=39 "ChilliCream")](https://github.com/ChilliCream)
[![4OTC](https://avatars.githubusercontent.com/u/68428092?v=4&s=39 "4OTC")](https://github.com/4OTC)
[![domischell](https://avatars.githubusercontent.com/u/66068846?u=0a5c5e2e7d90f15ea657bc660f175605935c5bea&v=4&s=39 "domischell")](https://github.com/DominicSchell)
[![Adrian Alonso](https://avatars.githubusercontent.com/u/2027083?u=129cf516d99f5cb2fd0f4a0787a069f3446b7522&v=4&s=39 "Adrian Alonso")](https://github.com/adalon)
[![torutek](https://avatars.githubusercontent.com/u/33917059?v=4&s=39 "torutek")](https://github.com/torutek)
[![mccaffers](https://avatars.githubusercontent.com/u/16667079?u=110034edf51097a5ee82cb6a94ae5483568e3469&v=4&s=39 "mccaffers")](https://github.com/mccaffers)
[![Seika Logiciel](https://avatars.githubusercontent.com/u/2564602?v=4&s=39 "Seika Logiciel")](https://github.com/SeikaLogiciel)
[![Andrew Grant](https://avatars.githubusercontent.com/devlooped-user?s=39 "Andrew Grant")](https://github.com/wizardness)
[![Lars](https://avatars.githubusercontent.com/u/1727124?v=4&s=39 "Lars")](https://github.com/latonz)


<!-- sponsors.md -->
[![Sponsor this project](https://avatars.githubusercontent.com/devlooped-sponsor?s=118 "Sponsor this project")](https://github.com/sponsors/devlooped)

[Learn more about GitHub Sponsors](https://github.com/sponsors)

<!-- https://github.com/devlooped/sponsors/raw/main/footer.md -->

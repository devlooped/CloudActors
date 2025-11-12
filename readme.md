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
box by Orleans, Cloud Actors offers a message-passing style of programming 
with a uniform API to access actors: Execute and Query. 

These uniform operations receive a message (a.k.a. command or query) and 
optionally return a result. Consumers always use the same API to invoke 
operations on actors, and the combination of the actor id and the message 
consitute enough information to route the message to the right actor.

Actors can be implemented as plain CLR objects, with no need to inherit 
from any base class or implement any interface. The Orleans plumbing of 
grains and their activation is completely hidden from the developer.

## Features

Rather than relying on `dynamic` dispatch, this implementation relies heavily on source generators 
to provide strong-typed routing of messages, while preserving a flexible mechanism for implementors.

In addition, this library makes the grains completely transparent to the developer. They don't even 
need to take a dependency on Orleans. In other words: the developer writes his business logic as 
a plain CLR object (POCO).

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
public class Account    // 👈 no need to inherit or implement anything by default
{
    public Account(string id) => Id = id;       // 👈 no need for parameterless constructor

    public string Id { get; }
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
        Balance +-= command.Amount;
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

> NOTE: properties with private setters do not need any additional attributes in order 
> to be properly deserialized when reading the latest state from storage. A source generator 
> encapsulates all state for use in (de)serialization operations.

On the hosting side, an `AddCloudActors` extension method is provided to register the 
automatically generated grains to route invocations to the actors:

```csharp
var builder = WebApplication.CreateSlimBuilder(args);

builder.Host.UseOrleans(silo =>
{
    silo.UseLocalhostClustering();
    // 👇 registers generated grains, actor bus and activation features
    silo.AddCloudActors(); 
});
```

## State Deserialization

The above `Account` class only provides a single constructor receiving the account 
identifier. After various operations are performed on it, however, the state will 
be changed via private property setters (or direct field mutation). When you annotate 
a class with the `[Actor]` attribute, a source generator will create an inner class to 
hold all state properties (and fields), and implement (explicitly) an `IActor<TState>` 
interface to allow getting/setting the instance state.

This provides seamless integration with Orleans' recommended `IPersistentState<T>` 
injection mechanism, as shown in the generated grain above.

The generated state class for the above `Account` actor looks like this:
```csharp
partial class Account : IActor<Account.ActorState>
{
    ActorState? state;

    ActorState IActor<ActorState>.GetState()
    {
        state ??= new ActorState();
        state.Balance = Balance;
        state.IsClosed = IsClosed;
        state.Reason = Reason;
        return state;
    }

    ActorState IActor<ActorState>.SetState(ActorState state)
    {
        this.state = state;
        Balance = state.Balance;
        IsClosed = state.IsClosed;
        Reason = state.Reason;
        return state;
    }

    [GeneratedCode("Devlooped.CloudActors")]
    [GenerateSerializer]
    public partial class ActorState : IActorState<Account>
    {
        [Id(0)]
        public decimal Balance;
        [Id(1)]
        public bool IsClosed;
        [Id(2)]
        public CloseReason Reason;
    }
}
```

This is a sort of typed [Memento pattern](https://grokipedia.com/page/Memento_pattern) which allows 
the Orleans state persistence mechanisms to read and write the actor state without requiring 
any additional code from the developer. 

> [!NOTE]
> This code is automatically guaranteed to be in sync with the actor's properties and fields, 
> since it's generated at compile time. 

The explicit implementation of `IActor<TState>` also ensures that the actor's public API is not 
polluted with these methods.

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

Optimistic concurrency is implemented by exposing the stream version as the `IGranState.ETag` 
and parsing it when persisting to ensure consistency.

Users are free to implement this interface in any way they deem fit, but the library provides 
a default implementation if the interface is inherited but not implemented. The generated 
implementation provides a `Raise<T>(@event)` method for the actor's methods to raise events, 
and invokes provided `Apply(@event)` methods to apply the events to the state. The generator 
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

    // 👇 generated generic Apply dispatches to each based on event type

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

Note how the interface has no implementation in the actor itself. The implementation 
provided by the generator looks like the following:

```csharp
partial class Account
{
    List<object>? events;

    IReadOnlyList<object> IEventSourced.Events => events ??= new List<object>();

    void IEventSourced.AcceptEvents() => events?.Clear();

    void IEventSourced.LoadEvents(IEnumerable<object> history)
    {
        foreach (var @event in history)
        {
            Apply(@event);
        }
    }

    /// <summary>
    /// Applies an event. Invoked automatically when raising or loading events. 
    /// Do not invoke directly.
    /// </summary>
    void Apply(object @event)
    {
        switch (@event)
        {
            case Tests.Deposited e:
                Apply(e);
                break;
            case Tests.Withdrawn e:
                Apply(e);
                break;
            case Tests.Closed e:
                Apply(e);
                break;
            default:
                throw new NotSupportedException();
        }
    }

    partial void Apply(Deposited e);
    partial void Apply(Withdrawn e);
    partial void Apply(Closed e);

    /// <summary>
    /// Raises and applies a new event of the specified type.
    /// See <see cref="Raise{T}(T)"/>.
    /// </summary>
    void Raise<T>() where T : notnull, new() => Raise(new T());

    /// <summary>
    /// Raises and applies an event.
    /// </summary>
    void Raise<T>(T @event) where T : notnull
    {
        Apply(@event);
        (events ??= new List<object>()).Add(@event);
    }
}
```

> [!NOTE]
> Note how there's no dynamic dispatch here 💯.

An important colorary of this project is that the design of a library and particularly 
its implementation details, will vary greatly if it can assume source generators will 
play a role in its consumption. In this particular case, many design decisions 
were different initially before I had the generators in place, and the result afterwards 
was a simplification in many aspects, with less base types in the main library/interfaces 
project, and more incremental behavior addded as users opt-in to certain features.

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

For the above actor, the generated grain looks like this:

```csharp
public partial class AccountGrain : Grain, IActorGrain
{
    // 👇 uses recommended injected state approach
    readonly IActorPersistentState<Account.ActorState, Account> storage;

    // 👇 use [Actor("stateName", "storageProvider")] on actor to customize this
    public AccountGrain([PersistentState(nameof(Account))] IPersistentState<Account.ActorState> storage)
        => this.storage = storage as IActorPersistentState<Account.ActorState, Account> ?? 
            throw new ArgumentException("Unsupported persistent state");

    [ReadOnly]
    public Task<TResult> QueryAsync<TResult>(IActorQuery<TResult> command)
    {
        switch (command)
        {
            case Tests.GetBalance query:
                return Task.FromResult((TResult)(object)storage.Actor.Query(query));
            default:
                throw new NotSupportedException();
        }
    }

    public async Task<TResult> ExecuteAsync<TResult>(IActorCommand<TResult> command)
    {
        switch (command)
        {
            case Tests.Close cmd:
                var result = await storage.Actor.CloseAsync(cmd);
                try
                {
                    await storage.WriteStateAsync();
                }
                catch 
                {
                    await storage.ReadStateAsync(); // 👈 rollback state on failure
                    throw;
                }
                return (TResult)(object)result;
            default:
                throw new NotSupportedException();
        }
    }

    public async Task ExecuteAsync(IActorCommand command)
    {
        switch (command)
        {
            case Tests.Deposit cmd:
                await storage.Actor.DepositAsync(cmd);
                try
                {
                    await storage.WriteStateAsync();
                }
                catch 
                {
                    await storage.ReadStateAsync();
                    throw;
                }
                break;
            case Tests.Withdraw cmd:
                storage.Actor.Execute(cmd);
                try
                {
                    await storage.WriteStateAsync();
                }
                catch 
                {
                    await storage.ReadStateAsync();
                    throw;
                }
                break;
            case Tests.Close cmd:
                await storage.Actor.CloseAsync(cmd);
                try
                {
                    await storage.WriteStateAsync();
                }
                catch 
                {
                    await storage.ReadStateAsync();
                    throw;
                }
                break;
            default:
                throw new NotSupportedException();
        }
    }
}
```

Note how the grain is a partial class, so you can add your own methods to it. The generated
code also uses whichever method names (and overloads) you used in your actor class to handle 
the incoming messages, so it doesn't impose any particular naming convention.

> [!NOTE]
> Note how there's no dynamic dispatch here either 💯.

Since the grain metadata/registry is generated by a source generator, and source generators 
[can't depend on other generated code](https://github.com/dotnet/roslyn/issues/57239) the 
type metadata won't be available automatically, even if we generate types inheriting from 
`Grain` which is typically enough. For that reason, a separate generator emits the 
`AddCloudActors` extension method, which properly registers these types with Orleans. The 
generated extension method looks like the following (usage shown already above when 
configuring Orleans):

```csharp
namespace Orleans.Runtime
{
    public static class CloudActorsExtensions
    {
        public static ISiloBuilder AddCloudActors(this ISiloBuilder builder)
        {
            builder.Configure<GrainTypeOptions>(options => 
            {
                // 👇 registers each generated grain type
                options.Classes.Add(typeof(Tests.AccountGrain));
            });

            builder.ConfigureServices(services =>
            {
                // 👇 registers IActorBus and actor activation features
                services.AddCloudActors();
            });

            return builder;
        }
    }
}
```

Finally, in order to improve discoverability for consumers of the `IActorBus` interface,
extension method overloads will be generated that surface the available actor messages 
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
[![DRIVE.NET, Inc.](https://avatars.githubusercontent.com/u/15047123?v=4&s=39 "DRIVE.NET, Inc.")](https://github.com/drivenet)
[![Keith Pickford](https://avatars.githubusercontent.com/u/16598898?u=64416b80caf7092a885f60bb31612270bffc9598&v=4&s=39 "Keith Pickford")](https://github.com/Keflon)
[![Thomas Bolon](https://avatars.githubusercontent.com/u/127185?u=7f50babfc888675e37feb80851a4e9708f573386&v=4&s=39 "Thomas Bolon")](https://github.com/tbolon)
[![Kori Francis](https://avatars.githubusercontent.com/u/67574?u=3991fb983e1c399edf39aebc00a9f9cd425703bd&v=4&s=39 "Kori Francis")](https://github.com/kfrancis)
[![Uno Platform](https://avatars.githubusercontent.com/u/52228309?v=4&s=39 "Uno Platform")](https://github.com/unoplatform)
[![Reuben Swartz](https://avatars.githubusercontent.com/u/724704?u=2076fe336f9f6ad678009f1595cbea434b0c5a41&v=4&s=39 "Reuben Swartz")](https://github.com/rbnswartz)
[![Jacob Foshee](https://avatars.githubusercontent.com/u/480334?v=4&s=39 "Jacob Foshee")](https://github.com/jfoshee)
[![](https://avatars.githubusercontent.com/u/33566379?u=bf62e2b46435a267fa246a64537870fd2449410f&v=4&s=39 "")](https://github.com/Mrxx99)
[![Eric Johnson](https://avatars.githubusercontent.com/u/26369281?u=41b560c2bc493149b32d384b960e0948c78767ab&v=4&s=39 "Eric Johnson")](https://github.com/eajhnsn1)
[![David JENNI](https://avatars.githubusercontent.com/u/3200210?v=4&s=39 "David JENNI")](https://github.com/davidjenni)
[![Jonathan ](https://avatars.githubusercontent.com/u/5510103?u=98dcfbef3f32de629d30f1f418a095bf09e14891&v=4&s=39 "Jonathan ")](https://github.com/Jonathan-Hickey)
[![Charley Wu](https://avatars.githubusercontent.com/u/574719?u=ea7c743490c83e8e4b36af76000f2c71f75d636e&v=4&s=39 "Charley Wu")](https://github.com/akunzai)
[![Ken Bonny](https://avatars.githubusercontent.com/u/6417376?u=569af445b6f387917029ffb5129e9cf9f6f68421&v=4&s=39 "Ken Bonny")](https://github.com/KenBonny)
[![Simon Cropp](https://avatars.githubusercontent.com/u/122666?v=4&s=39 "Simon Cropp")](https://github.com/SimonCropp)
[![agileworks-eu](https://avatars.githubusercontent.com/u/5989304?v=4&s=39 "agileworks-eu")](https://github.com/agileworks-eu)
[![Zheyu Shen](https://avatars.githubusercontent.com/u/4067473?v=4&s=39 "Zheyu Shen")](https://github.com/arsdragonfly)
[![Vezel](https://avatars.githubusercontent.com/u/87844133?v=4&s=39 "Vezel")](https://github.com/vezel-dev)
[![ChilliCream](https://avatars.githubusercontent.com/u/16239022?v=4&s=39 "ChilliCream")](https://github.com/ChilliCream)
[![4OTC](https://avatars.githubusercontent.com/u/68428092?v=4&s=39 "4OTC")](https://github.com/4OTC)
[![Vincent Limo](https://avatars.githubusercontent.com/devlooped-user?s=39 "Vincent Limo")](https://github.com/v-limo)
[![domischell](https://avatars.githubusercontent.com/u/66068846?u=0a5c5e2e7d90f15ea657bc660f175605935c5bea&v=4&s=39 "domischell")](https://github.com/DominicSchell)
[![Justin Wendlandt](https://avatars.githubusercontent.com/u/1068431?u=f7715ed6a8bf926d96ec286f0f1c65f94bf86928&v=4&s=39 "Justin Wendlandt")](https://github.com/jwendl)
[![Adrian Alonso](https://avatars.githubusercontent.com/u/2027083?u=129cf516d99f5cb2fd0f4a0787a069f3446b7522&v=4&s=39 "Adrian Alonso")](https://github.com/adalon)
[![Michael Hagedorn](https://avatars.githubusercontent.com/u/61711586?u=8f653dfcb641e8c18cc5f78692ebc6bb3a0c92be&v=4&s=39 "Michael Hagedorn")](https://github.com/Eule02)
[![torutek](https://avatars.githubusercontent.com/u/33917059?v=4&s=39 "torutek")](https://github.com/torutek)
[![mccaffers](https://avatars.githubusercontent.com/u/16667079?u=739e110e62a75870c981640447efa5eb2cb3bc8f&v=4&s=39 "mccaffers")](https://github.com/mccaffers)


<!-- sponsors.md -->
[![Sponsor this project](https://avatars.githubusercontent.com/devlooped-sponsor?s=118 "Sponsor this project")](https://github.com/sponsors/devlooped)

[Learn more about GitHub Sponsors](https://github.com/sponsors)

<!-- https://github.com/devlooped/sponsors/raw/main/footer.md -->

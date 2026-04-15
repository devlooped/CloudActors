---
name: cloudactors
description: >
  Helps define, implement, and consume Cloud Actors — a message-passing actor library built on 
  Microsoft Orleans. Use this skill when working with actors, commands, queries, actor buses, 
  typed IDs, event sourcing, or Orleans hosting configuration in a CloudActors-based project.
---

# Cloud Actors

Cloud Actors is an opinionated, simplified actor library for .NET built on Microsoft Orleans. 
It replaces Orleans' RPC-style grain API with a uniform message-passing style: all actor 
interactions go through a single `IActorBus` with just two operations: **Execute** and **Query**.

## Core Concepts

### Actor Bus

The entry point for all actor interactions:

```csharp
public interface IActorBus
{
    Task ExecuteAsync(string id, IActorCommand command);
    Task<TResult> ExecuteAsync<TResult>(string id, IActorCommand<TResult> command);
    Task<TResult> QueryAsync<TResult>(string id, IActorQuery<TResult> query);
}
```

- `ExecuteAsync` — sends a command to an actor (state-changing operation)
- `QueryAsync` — sends a query to an actor (read-only, maps to `[ReadOnly]` Orleans grain method)
- `id` — a string identifying the actor instance, e.g. `"account/42"`

Inject `IActorBus` anywhere in the application to interact with actors.

### Messages

Messages (command or query) are plain records (or classes) implementing one of three marker interfaces:

```csharp
public partial record Deposit(decimal Amount) : IActorCommand;           // void command
public partial record Withdraw(decimal Amount) : IActorCommand;
public partial record Close(CloseReason Reason) : IActorCommand<decimal>; // value-returning command
public partial record GetBalance() : IActorQuery<decimal>;                // read-only query
```

- `IActorCommand` — void command (state-changing, no return value)
- `IActorCommand<TResult>` — command (state-changing) that returns a value
- `IActorQuery<TResult>` — read-only query (maps to `[ReadOnly]` grain method)

### Actors

Actors are plain C# (partial) classes annotated with `[Actor]`. No base class or interface required:

```csharp
[Actor]
public partial class Account
{
    public Account(string id) => Id = id;   // id injected by the framework

    public string Id { get; }
    public decimal Balance { get; private set; }
    public bool IsClosed { get; private set; }

    public Task DepositAsync(Deposit command)   // method name can be anything
    {
        Balance += command.Amount;
        return Task.CompletedTask;
    }

    public void Execute(Withdraw command)       // sync methods are fine too
    {
        Balance -= command.Amount;
    }

    public decimal Close(Close command)         // value-returning command handler
    {
        var balance = Balance;
        Balance = 0;
        IsClosed = true;
        return balance;
    }

    public decimal Query(GetBalance _) => Balance;  // query handler (read-only)
}
```

Key rules:
- Class must be annotated with `[Actor]`
- Constructor receives the `string id` as first parameter (injected automatically)
- Methods handle messages by matching a single parameter of the message type
- Method names are free — the source generator maps by parameter type
- Methods can be sync or async; `Task`/`Task<T>` both supported
- Writable instance properties and mutable instance fields are included in the generated snapshot state

### State Serialization

The `[Actor]` source generator emits a nested `ActorState` type and implements `IActor<TState>` by copying values between the actor instance and that snapshot.

- Included in `ActorState`: non-indexer instance properties with a setter, plus mutable instance fields
- Excluded from `ActorState`: static members, `const` fields, `readonly` fields, and get-only properties
- The generated grain injects `IPersistentState<ActorState>` and calls `ReadStateAsync()` on activation
- When a persisted record exists, the state is applied back to the actor through the generated `SetState(...)` method; if no record exists, constructor-initialized values stay in place
- After each command, the grain calls the generated `GetState()` method and writes that snapshot through Orleans persistence
- `stateName` and `storageProvider` still come from `[Actor(...)]`, just like ordinary Orleans persistent state

### Event Sourcing

Actors can opt into event sourcing by inheriting `IEventSourced` (without implementing it):

```csharp
[Actor]
public partial class Account : IEventSourced  // not implemented — code-generated
{
    public void Execute(Deposit command) => Raise(new Deposited(command.Amount));
    public void Execute(Withdraw command) => Raise(new Withdrawn(command.Amount));

    partial void Apply(Deposited e) => Balance += e.Amount;     // Apply methods route events
    partial void Apply(Withdrawn e) => Balance -= e.Amount;
}
```

The source generator provides `Raise<T>(event)` and routes events to matching `Apply(TEvent)` methods.

Completion in your editor will provide the relevant Apply methods to implement as soon as you type `partial ` and trigger completion.

### Actor IDs

Actors can use non-string typed IDs, either primitive or custom types (e.g. `ProductId`) as long as they are `IParsable<T>`. 
The framework handles conversion to/from string for Orleans.

> We recommend using the `StructId` nuget package for strongly-typed IDs,
> but third party ones are also supported.

When an actor constructor uses a primitive ID such as `long` or `Guid`, CloudActors still generates a typed wrapper for that actor ID instead of exposing the raw primitive directly on `IActorBus`. That means `Order(long id)` gets an `Order.OrderId`, `Catalog(long id)` gets a `Catalog.CatalogId`, and you can use the generated bus overloads with those wrapper types rather than building the actor's full ID such as `order/{longId}`.

Use the generated `{Actor}.NewId(...)` helper to create those IDs and call the typed overloads safely:

```csharp
var orderId = Order.NewId(42L);
var catalogId = Catalog.NewId(42L);

await bus.ExecuteAsync(orderId, new SetTotal(19.99m));
await bus.QueryAsync(catalogId, GetDescription.Default);
```

This avoids a common mistake with raw strings or primitives: the same underlying value can be valid for multiple actor types, but the generated wrapper makes `Order.NewId(42L)` and `Catalog.NewId(42L)` different types, so you cannot accidentally pass one actor's ID to another actor's `ExecuteAsync` or `QueryAsync` overload.

### Typed Actor IDs

Instead of raw `string` IDs (or other primitive types), actors can use typed IDs (via [StructId](https://github.com/devlooped/StructId) or a similar library) as long as they support `IParsable<T>/IFormattable`:

```csharp
[Actor]
public class Product(ProductId id) // typed ID injected
{
    public ProductId Id { get; } = id;
    // ...
}
```

The source generator emits strongly-typed `IActorBus` overloads for each actor with a unique typed ID:

```csharp
await bus.ExecuteAsync(new ProductId("p1"), new UpdatePrice(9.99m));
await bus.QueryAsync<decimal>(new ProductId("p1"), new GetPrice());
```

Typed IDs must be `IParsable<T>/IFormattable` for the framework to convert to/from string. Virtually 
all structured id packages will automatically generate implementations of these interfaces automatically.

If StructId package is used, load and use its `structid` skill.

## Setup and Hosting (Orleans)

Add CloudActors to an Orleans silo:

```csharp
builder.Host.UseOrleans(silo =>
{
    silo.UseLocalhostClustering();
    silo.AddCloudActors();   // registers generated grains, actor bus, and activation features
});
```

The `AddCloudActors()` extension:
- Registers all generated Orleans grains (one per actor type)
- Registers `IActorBus` in DI
- Wires up typed ID resolution via `IActorIdFactory`

## Code Generation

The library uses Roslyn source generators. When building a project that references `Devlooped.CloudActors.Abstractions`:

- `[Actor]`-annotated classes get a generated `ActorState` inner class + `IActor<TState>` implementation
- `IEventSourced` inherited (not implemented) classes get `Raise<T>()` + event routing
- A per-assembly grain registration module is generated for `AddCloudActors()` discovery
- Typed ID overloads for `IActorBus` are emitted for actors with unique typed ID constructors

## Requirements

The actors should be defined in a class library that references `Devlooped.CloudActors.Abstractions`. The hosting project (e.g. the Orleans silo) should reference that class library and call `AddCloudActors()` in the Orleans configuration, and reference the `Devlooped.CloudActors` package. 

Consumers of the actors (e.g. API controllers) should also reference the class library to get access to the message types and `IActorBus`.

## Conventions

- Actor message types should be `partial record` (allows source generators to extend them)
- Actor classes should be `partial` when using event sourcing
- Actor IDs are strings at the Orleans level; typed IDs are helpers on top
- Use `IActorCommand` for state-changing operations, `IActorQuery` for reads
- The `[Actor]` attribute accepts optional `stateName` and `storageProvider` parameters
- One actor class = one Orleans grain type; the grain is fully transparent to the developer
- Business logic inside the actors should be independent of Orleans; the actors are just plain C# classes. The Orleans-specific code is generated by the source generator and lives in the background, so you can focus on your domain logic without coupling to Orleans APIs directly.
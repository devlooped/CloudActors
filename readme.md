# Cloud Native Actors

An opinionated, simplified and uniform Cloud Native actors' library that integrates with Microsoft Orleans.

## Motivation

Watch the [Orleans Virtual Meetup 7](https://www.youtube.com/watch?v=FKL-PS8Q9ac) where Yevhen 
(of [Streamstone](https://github.com/yevhen/Streamstone) fame) makes the case for using message
passing style with actors instead of the more common RPC style offered by Orleans.

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
[GenerateSerializer]
public partial record Deposit(decimal Amount) : IActorCommand;  // 👈 marker interface for void commands

[GenerateSerializer]
public partial record Withdraw(decimal Amount) : IActorCommand;

[GenerateSerializer]
public partial record Close() : IActorCommand<decimal>;         // 👈 marker interface for value-returning commands

[GenerateSerializer]
public partial record GetBalance() : IActorQuery<decimal>;      // 👈 marker interface for queries (a.k.a. readonly methods)
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
    public decimal Close(Close _)
    {
        var balance = Balance;
        Balance = 0;
        IsClosed = true;
        return balance;
    }

    // Showcases a query that doesn't change state
    public decimal Query(GetBalance _) => Balance;  // 👈 becomes [ReadOnly] grain operation
}
```

On the hosting side, an `AddCloudActors` extension method is provided to register the 
automatically generated grains to route invocations to the actors:

```csharp
var builder = WebApplication.CreateSlimBuilder(args);

builder.Host.UseOrleans(silo =>
{
    silo.UseLocalhostClustering();
    silo.AddMemoryGrainStorageAsDefault();
    silo.AddCloudActors();  // 👈 registers generated grains
});
```

Finally, you need to hook up the `IActorBus` service and related functionality with:

```csharp
builder.Services.UseCloudActors();  // 👈 registers bus and activation features
```

## How it works

The library uses source generators to generate the grain classes. It's easy to inspect the 
generated code by setting the `EmitCompilerGeneratedFiles` property to `true` in the project 
and inspecting the `obj` folder.

For the above actor, the generated grain looks like this:

```csharp
public partial class AccountGrain : Grain, IActorGrain
{
    readonly IPersistentState<Account> storage; // 👈 uses recommended injected state approach

    // 👇 use [Actor("stateName", "storageName")] on actor to customize this
    public AccountGrain([PersistentState] IPersistentState<Account> storage) 
        => this.storage = storage;

    [ReadOnly]
    public Task<TResult> QueryAsync<TResult>(IActorQuery<TResult> command)
    {
        switch (command)
        {
            case Tests.GetBalance query:
                return Task.FromResult((TResult)(object)storage.State.Query(query));
            default:
                throw new NotSupportedException();
        }
    }

    public async Task<TResult> ExecuteAsync<TResult>(IActorCommand<TResult> command)
    {
        switch (command)
        {
            case Tests.Close cmd:
                var result = await storage.State.CloseAsync(cmd);
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
                await storage.State.DepositAsync(cmd);
                try
                {
                    await storage.WriteStateAsync();
                }
                catch 
                {
                    await storage.ReadStateAsync(); // 👈 rollback state on failure
                    throw;
                }
                break;
            case Tests.Withdraw cmd:
                storage.State.Execute(cmd);
                try
                {
                    await storage.WriteStateAsync();
                }
                catch 
                {
                    await storage.ReadStateAsync(); // 👈 rollback state on failure
                    throw;
                }
                break;
            case Tests.Close cmd:
                await storage.State.CloseAsync(cmd);
                try
                {
                    await storage.WriteStateAsync();
                }
                catch 
                {
                    await storage.ReadStateAsync(); // 👈 rollback state on failure
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

            return builder;
        }
    }
}
```

Finally, in order to improve discoverability for consumers of the `IActorBus` interface,
extension method overloads will be generated that surface the available actor messages 
as non-generic overloads, such as:

![execute overloads](https://github.com/devlooped/CloudActors/blob/main/assets/img/command-overloads.png?raw=true)

![query overloads](https://github.com/devlooped/CloudActors/blob/main/assets/img/query-overloads.png?raw=true)

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

        Raise(new Withdrawn(command.Amount));
    }

    public decimal Execute(Close _)
    {
        if (IsClosed)
            throw new InvalidOperationException("Account is closed");

        var balance = Balance;
        Raise(new Closed(Balance));
        return balance;
    }

    public decimal Query(GetBalance _) => Balance;

    // 👇 generated generic Apply dispatches to each based on event type

    void Apply(Deposited @event) => Balance += @event.Amount;

    void Apply(Withdrawn @event) => Balance -= @event.Amount;

    void Apply(Closed @event)
    {
        Balance = 0;
        IsClosed = true;
    }
}
```

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

Note how there's no dynamic dispatch here either 💯.
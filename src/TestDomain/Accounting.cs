using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Devlooped.CloudActors;
using Moq;

namespace TestDomain;

// NOTE: there's NO references or dependencies to any Orleans concepts. Just
// pure CloudActors interfaces and attributes.


public partial record Deposit(decimal Amount) : IActorCommand;

public partial record Deposited(decimal Amount);

public partial record Withdraw(decimal Amount) : IActorCommand;

public partial record Withdrawn(decimal Amount);

public partial record JournaledDeposited(decimal Amount);

public partial record JournaledWithdrawn(decimal Amount);

public partial record StateStorageDeposited(decimal Amount);

public partial record StateStorageWithdrawn(decimal Amount);

public partial record LogStorageDeposited(decimal Amount);

public partial record LogStorageWithdrawn(decimal Amount);

public partial record Close(CloseReason Reason = CloseReason.Customer) : IActorCommand<decimal>;

public enum CloseReason
{
    Customer,
    Fraud,
    Other
}

public partial record Closed(decimal Balance, CloseReason Reason);

public partial record GetBalance() : IActorQuery<decimal>
{
    // optional singleton pattern for stateless records
    public static GetBalance Default { get; } = new();
}

[Actor]
public partial class Account : IEventSourced
{
    bool isNew = true;
    readonly IActorBus bus;

    public Account(string id) : this(id, Mock.Of<IActorBus>()) { }

    /// <summary>Showcases that DI ctor works too</summary>
    public Account(string id, IActorBus bus)
        => (Id, this.bus)
        = (id, bus);

    [IgnoreDataMember]
    public string Id { get; }

    public decimal Balance { get; private set; }

    public bool IsClosed { get; private set; }

    public CloseReason Reason { get; private set; }

    // Showcases that operation can also be just Execute overloads 
    //public void Execute(Deposit command)
    //{
    //    // validate command
    //    Raise(new Deposited(command.Amount));
    //}
    //public void Execute(Withdraw command)
    //{
    //    // validate command
    //    Raise(new Withdraw(command.Amount));
    //}

    // Showcases that operations can have a name that's not Execute
    public void Deposit(Deposit command)
    {
        if (IsClosed)
            throw new InvalidOperationException("Account is closed.");

        // validate command
        Raise(new Deposited(command.Amount));
    }

    // Showcases that operations don't have to be async
    public void Withdraw(Withdraw command)
    {
        if (IsClosed)
            throw new InvalidOperationException("Account is closed.");

        if (command.Amount > Balance)
            throw new InvalidOperationException("Insufficient funds.");

        Raise(new Withdrawn(command.Amount));
    }

    // Showcases value-returning async operation with custom name.
    public decimal Close(Close command)
    {
        var final = Balance;
        Raise(new Closed(Balance, command.Reason));
        return final;
    }

    // Showcases a query that doesn't change state, which becomes a [ReadOnly] grain operation.
    public decimal Query(GetBalance _) => Balance;

    partial void Apply(Deposited e) => Balance += e.Amount;
    partial void Apply(Withdrawn e) => Balance -= e.Amount;
    partial void Apply(Closed e)
    {
        Balance = 0;
        IsClosed = true;
        Reason = e.Reason;
    }

    public List<Type> Raised { get; } = new();

    partial void OnRaised<T>(T @event) where T : notnull => Raised.Add(typeof(T));
}

[Actor]
[Journaled("StateStorage")]
public partial class StateStorageJournaledAccount(string id) : IEventSourced
{
    [IgnoreDataMember]
    public string Id => id;

    public decimal Balance { get; private set; }

    public void Deposit(Deposit command)
    {
        if (command.Amount <= 0)
            throw new InvalidOperationException("Amount must be positive.");

        Raise(new StateStorageDeposited(command.Amount));
    }

    public void Withdraw(Withdraw command)
    {
        if (command.Amount <= 0)
            throw new InvalidOperationException("Amount must be positive.");

        if (command.Amount > Balance)
            throw new InvalidOperationException("Insufficient funds.");

        Raise(new StateStorageWithdrawn(command.Amount));
    }

    public decimal Query(GetBalance _) => Balance;

    partial void Apply(StateStorageDeposited e) => Balance += e.Amount;
    partial void Apply(StateStorageWithdrawn e) => Balance -= e.Amount;
}

[Actor]
[Journaled("LogStorage")]
public partial class LogStorageJournaledAccount(string id) : IEventSourced
{
    [IgnoreDataMember]
    public string Id => id;

    public decimal Balance { get; private set; }

    public void Deposit(Deposit command)
    {
        if (command.Amount <= 0)
            throw new InvalidOperationException("Amount must be positive.");

        Raise(new LogStorageDeposited(command.Amount));
    }

    public void Withdraw(Withdraw command)
    {
        if (command.Amount <= 0)
            throw new InvalidOperationException("Amount must be positive.");

        if (command.Amount > Balance)
            throw new InvalidOperationException("Insufficient funds.");

        Raise(new LogStorageWithdrawn(command.Amount));
    }

    public decimal Query(GetBalance _) => Balance;

    partial void Apply(LogStorageDeposited e) => Balance += e.Amount;
    partial void Apply(LogStorageWithdrawn e) => Balance -= e.Amount;
}

[Actor]
[Journaled]
public partial class JournaledAccount(string id) : IEventSourced
{
    [IgnoreDataMember]
    public string Id => id;

    public decimal Balance { get; private set; }

    public void Deposit(Deposit command)
    {
        if (command.Amount <= 0)
            throw new InvalidOperationException("Amount must be positive.");

        Raise(new JournaledDeposited(command.Amount));
    }

    public void Withdraw(Withdraw command)
    {
        if (command.Amount <= 0)
            throw new InvalidOperationException("Amount must be positive.");

        if (command.Amount > Balance)
            throw new InvalidOperationException("Insufficient funds.");

        Raise(new JournaledWithdrawn(command.Amount));
    }

    public decimal Query(GetBalance _) => Balance;

    partial void Apply(JournaledDeposited e) => Balance += e.Amount;
    partial void Apply(JournaledWithdrawn e) => Balance -= e.Amount;
}

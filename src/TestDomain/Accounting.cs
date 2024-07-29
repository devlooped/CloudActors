using System;
using System.Runtime.Serialization;
using Devlooped.CloudActors;
using Moq;
using Orleans;

namespace TestDomain;

public partial record Deposit(decimal Amount) : IActorCommand;

public partial record Deposited(decimal Amount);

public partial record Withdraw(decimal Amount) : IActorCommand;

public partial record Withdrawn(decimal Amount);

public partial record Close(CloseReason Reason = CloseReason.Customer) : IActorCommand<decimal>;

public enum CloseReason
{
    Customer,
    Fraud,
    Other
}

public partial record Closed(decimal Balance, CloseReason Reason);

public partial record GetBalance() : IActorQuery<decimal>;

[Actor]
public partial class Account : IEventSourced //, IActor
{
    bool isNew = true;
    readonly IActorBus bus;

    public Account(string id) : this(id, Mock.Of<IActorBus>()) { }

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
}
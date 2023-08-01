using System;
using System.Threading.Tasks;
using Devlooped.CloudActors;
using Xunit.Abstractions;

namespace Tests;

public class TestAccounts(ITestOutputHelper output)
{
    [Fact]
    public void Apply()
    {
        var account = new Account("asdf");
        account.Execute(new Deposit(100));

        account = new Account("asdf");
        ((IEventSourced)account).LoadEvents(new[] { new Deposited(100) });
    }

    [Fact]
    public async Task Execute()
    {
        IActorBus bus = null!;

        await bus.ExecuteAsync("asdf", new Deposit(100));

        var cmd = new Withdraw(100);
        await bus.ExecuteAsync("asdf", cmd);

        var balance = await bus.ExecuteAsync("asdf", new GetBalance());
    }
}

[ActorCommand]
public partial record Deposit(decimal Amount);

public partial record Deposited(decimal Amount);

[ActorCommand]
public partial record Withdraw(decimal Amount);

public partial record Withdrawn(decimal Amount);

[ActorCommand<decimal>]
public partial record GetBalance();

public partial class Account : EventSourced
{
    public string Id { get; }
    public decimal Balance { get; }

    public Account(string id) => Id = id;

    public void Execute(Deposit command)
    {
        // validate command
        // raise event
        Raise(new Deposited(command.Amount));
        //Apply(new AccountOpened(command.AccountId, command.Name));
    }

    void Apply(Deposited @event)
    {
    }

    void Apply(Withdrawn @event)
    {
    }

    protected override void Apply(object @event)
    {
        switch (@event)
        {
            case Deposited e:
                Apply(e);
                break;
            case Withdrawn e:
                Apply(e);
                break;
            default:
                throw new NotSupportedException();
        }
    }
}

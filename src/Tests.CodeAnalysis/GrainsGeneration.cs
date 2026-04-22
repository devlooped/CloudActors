using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Devlooped.CloudActors;
using Orleans.EventSourcing;
using Orleans.Runtime;

namespace Tests;

public class GrainsGeneration
{
    [Fact]
    public void GrainWithNoArgs()
    {
        var ctor = typeof(ActorWithNoArgsGrain).GetConstructors()[0];
        Assert.Single(ctor.GetParameters());

        var attr = ctor.GetParameters()[0].GetCustomAttribute<PersistentStateAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(nameof(ActorWithNoArgs), attr.StateName);
        Assert.Null(attr.StorageName);
    }

    [Fact]
    public void GrainWithStateName()
    {
        var ctor = typeof(ActorWithStateNameGrain).GetConstructors()[0];
        Assert.Single(ctor.GetParameters());

        var attr = ctor.GetParameters()[0].GetCustomAttribute<PersistentStateAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("Foo", attr.StateName);
        Assert.Null(attr.StorageName);
    }

    [Fact]
    public void GrainWithStorageProvider()
    {
        var ctor = typeof(ActorWithStorageProviderGrain).GetConstructors()[0];
        Assert.Single(ctor.GetParameters());

        var attr = ctor.GetParameters()[0].GetCustomAttribute<PersistentStateAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(nameof(ActorWithStorageProvider), attr.StateName);
        Assert.Equal("Bar", attr.StorageName);
    }

    [Fact]
    public void GrainWithStateNameAndStorageProvider()
    {
        var ctor = typeof(ActorWithStateAndStorageProviderGrain).GetConstructors()[0];
        Assert.Single(ctor.GetParameters());

        var attr = ctor.GetParameters()[0].GetCustomAttribute<PersistentStateAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("Foo", attr.StateName);
        Assert.Equal("Bar", attr.StorageName);
    }

    [Fact]
    public void JournaledActorGeneratesJournaledGrain()
    {
        Assert.Equal(typeof(JournaledGrain<JournaledActor.ActorState, object>), typeof(JournaledActorGrain).BaseType);
        Assert.False(typeof(IEventSourced).IsAssignableFrom(typeof(JournaledActor.ActorState)));
    }

    [Fact]
    public void EventSourcedActorGetsConfirmEventsBridge()
    {
        Assert.Contains(typeof(IConfirmableEvents), typeof(JournaledActor).GetInterfaces());
        Assert.NotNull(typeof(JournaledActor).GetMethod("ConfirmEvents", BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.Contains(typeof(IConfirmableEvents), typeof(StandardEventSourcedActor.ActorState).GetInterfaces());
        Assert.Null(typeof(ActorWithNoArgs).GetMethod("ConfirmEvents", BindingFlags.Instance | BindingFlags.NonPublic));
    }

    [Fact]
    public void JournaledAttributeSupportsBackgroundSave()
    {
        var attribute = typeof(BackgroundSaveJournaledActor).GetCustomAttribute<JournaledAttribute>();
        Assert.NotNull(attribute);
        Assert.True(attribute!.BackgroundSave);
        Assert.Equal(typeof(JournaledGrain<BackgroundSaveJournaledActor.ActorState, object>), typeof(BackgroundSaveJournaledActorGrain).BaseType);
    }
}

[Actor]
public partial class ActorWithNoArgs(string id)
{
    [IgnoreDataMember]
    public string Id => id;
}

[Actor("Foo")]
public partial class ActorWithStateName(string id)
{
    [IgnoreDataMember]
    public string Id => id;
}

[Actor(storageProvider: "Bar")]
public partial class ActorWithStorageProvider(string id)
{
    [IgnoreDataMember]
    public string Id => id;
}

[Actor("Foo", "Bar")]
public partial class ActorWithStateAndStorageProvider(string id)
{
    [IgnoreDataMember]
    public string Id => id;
}

[Actor]
[Journaled]
public partial class JournaledActor(string id) : IEventSourced
{
    [IgnoreDataMember]
    public string Id => id;

    public decimal Balance { get; private set; }

    public void Deposit(AddFunds _) => Raise(new JournaledDeposited(10));

    public decimal Query(GetBalance _) => Balance;

    partial void Apply(JournaledDeposited e) => Balance += e.Amount;
}

[Actor]
public partial class StandardEventSourcedActor(string id) : IEventSourced
{
    [IgnoreDataMember]
    public string Id => id;

    public decimal Balance { get; private set; }

    public void Deposit(AddFunds _) => Raise(new StandardEventSourcedDeposited(10));

    partial void Apply(StandardEventSourcedDeposited e) => Balance += e.Amount;
}

[Actor]
[Journaled(backgroundSave: true)]
public partial class BackgroundSaveJournaledActor(string id) : IEventSourced
{
    [IgnoreDataMember]
    public string Id => id;

    public decimal Balance { get; private set; }

    public async Task Deposit(AddFunds _)
    {
        Raise(new BackgroundSaveJournaledDeposited(10));
        await ConfirmEvents();
    }

    public decimal Query(GetBalance _) => Balance;

    partial void Apply(BackgroundSaveJournaledDeposited e) => Balance += e.Amount;
}

public partial record AddFunds() : IActorCommand;
public partial record GetBalance() : IActorQuery<decimal>;
public partial record JournaledDeposited(decimal Amount);
public partial record StandardEventSourcedDeposited(decimal Amount);
public partial record BackgroundSaveJournaledDeposited(decimal Amount);

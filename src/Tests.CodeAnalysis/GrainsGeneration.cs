using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using Devlooped.CloudActors;
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
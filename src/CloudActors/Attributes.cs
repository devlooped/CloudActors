using System;

namespace Devlooped.CloudActors;

[AttributeUsage(AttributeTargets.Class)]
public class ActorAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Class)]
public class ActorCommandAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Class)]
public class ActorCommandAttribute<TResult> : Attribute
{
}

[AttributeUsage(AttributeTargets.Class)]
public class ActorQueryAttribute<TResult> : Attribute
{
}
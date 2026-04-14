using System;
using Devlooped.CloudActors;
using StructId;

namespace TestDomain;

// Strongly-typed ID using StructId with Guid value
public readonly partial record struct ProductId : IStructId<Guid>;

public partial record SetPrice(decimal Price) : IActorCommand;

public partial record GetPrice() : IActorQuery<decimal>
{
    public static GetPrice Default { get; } = new();
}

[Actor]
public partial class Product(ProductId id)
{
    public ProductId Id => id;

    public decimal Price { get; private set; }

    public void Execute(SetPrice command) => Price = command.Price;

    public decimal Query(GetPrice _) => Price;
}

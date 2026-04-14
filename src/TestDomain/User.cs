using StronglyTypedIds;

namespace TestDomain;

// Strongly-typed ID using StronglyTypedId with long value
[StronglyTypedId(Template.Long)]
public readonly partial struct UserId;

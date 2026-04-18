namespace Kun.Sample.Catalog.Domain;

/// <summary>
/// Value object representing a product category.
/// Equality is structural (Id + Name), not identity-based.
/// </summary>
public sealed record Category(Guid Id, string Name);

using System.Text.Json.Serialization;
using Kun.Sample.Catalog.Application.Commands;
using Kun.Sample.Catalog.Application.Queries;

namespace Kun.Sample.Catalog;

/// <summary>
/// Source-generated JSON serializer context for Catalog API types.
/// Required by <c>MapCommand</c> / <c>MapQuery</c> for AOT-safe endpoint binding.
/// Uses camelCase naming policy so REST clients can post camelCase JSON payloads.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(CreateProductCommand))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(ProductDto))]
internal sealed partial class CatalogJsonContext : JsonSerializerContext;

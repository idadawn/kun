using System.Text.Json.Serialization;

namespace Kun.Sample.Order.Application.Sagas;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for AOT-safe saga context serialization.
/// Required by <see cref="Kun.Saga.DependencyInjection.KunSagaBuilder.AddSaga{TSaga,TContext}"/>.
/// </summary>
[JsonSerializable(typeof(OrderCreationSagaContext))]
[JsonSerializable(typeof(OrderLineDto))]
public sealed partial class OrderJsonContext : JsonSerializerContext;

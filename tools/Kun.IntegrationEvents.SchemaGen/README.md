# Kun.IntegrationEvents.SchemaGen

Console tool that generates one draft-style JSON Schema per integration event
registered in a consumer's `IEventTypeRegistry`.

## Why

Consumers (SDKs, other services, external integrators) need to validate the
payloads they receive on `OrderPlaced` / `ProductPublished` / … without running
the publishing service's code. A JSON-Schema export gives them a per-event
contract that plugs into any standard schema validator and that survives the
publisher's refactors as long as the stable event ID stays constant.

## Usage

```bash
dotnet run --project tools/Kun.IntegrationEvents.SchemaGen -- \
    --assembly ./path/to/MyService.dll \
    --builder  MyService.KunEventRegistryBuilder::Build \
    --out      ./schemas
```

Flags:

| Flag | Required | Description |
|---|---|---|
| `--assembly` | yes | Path to the consumer's compiled output DLL. |
| `--builder` | yes | `Namespace.Type::StaticMethod` identifying a public static zero-arg method that returns an `IEventTypeRegistry`. |
| `--out` | yes | Directory to write `{eventId}.schema.json` into; created if missing. |
| `--indent` | no | Pretty-print the JSON (default: compact). |
| `--overwrite` | no | Replace pre-existing files (default: fail if file exists). |

## Consumer setup

The tool deliberately doesn't reach into your DI composition or guess
conventions. Provide a tiny factory method on a class of your choice:

```csharp
namespace MyService;

public static class KunEventRegistryBuilder
{
    public static IEventTypeRegistry Build()
    {
        var services = new ServiceCollection();
        services.AddKunEventBus(bus =>
        {
            bus.Register<OrderPlacedIntegrationEvent>("order.placed.v1");
            bus.Register<InventoryAdjustedIntegrationEvent>("inventory.adjusted.v1");
        });
        return services.BuildServiceProvider().GetRequiredService<IEventTypeRegistry>();
    }
}
```

Keep this method **idempotent** and free of side-effects (DB calls, network
I/O) — it runs inside the schema-gen tool, not inside your service.

## Output

The tool emits one file per registered event:

```
schemas/
├── inventory.adjusted.v1.schema.json
└── order.placed.v1.schema.json
```

Each file is a `JsonSchema.Net`-generated schema derived from the CLR type.
Property names follow the System.Text.Json mapping (honouring
`[JsonPropertyName]` and nullability annotations). Enums render as union of
the declared values. Nested records / classes recurse.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | Argument parsing failure — usage printed to stderr. |
| `2` | Assembly path does not exist. |
| `3` | Target file already exists and `--overwrite` was not supplied. |

Other errors (type not found, method not found, method returned null or a
non-registry) surface as uncaught `InvalidOperationException` with a
descriptive message — standard `dotnet run` exit code 1 applies.

## Recommended automation

Wire into CI after the consumer's release build:

```yaml
- name: Export integration event schemas
  run: |
    dotnet build MyService.csproj -c Release --no-restore
    dotnet run --project tools/Kun.IntegrationEvents.SchemaGen -- \
      --assembly MyService/bin/Release/net10.0/MyService.dll \
      --builder  MyService.KunEventRegistryBuilder::Build \
      --out      artifacts/schemas --indent --overwrite
    ls -la artifacts/schemas
```

Publish `artifacts/schemas` alongside your NuGet packages or release
artifacts so downstream consumers pull them with a single `curl`.

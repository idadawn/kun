<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-23 -->

# Kun.Configuration

## Purpose

Typed-options validation for Kun consumers. Wraps the .NET Options
pattern (`AddOptions<T>().BindConfiguration().ValidateDataAnnotations()
.ValidateOnStart()`) behind a single call and produces
**section-qualified** error messages so startup failures read like
`Authentik:ClientId is required but missing in configuration` instead
of the default `The ClientId field is required.`.

```csharp
services.AddKunOptions<AuthentikOptions>("Authentik");
```

Combined with `[RequiredConfig]`, `[UrlConfig]`, `[NonEmptyStringConfig]`,
consumers get fail-fast startup on typos, missing secrets, or
malformed URLs — with the exact config path in the error.

## Layer and AOT

- Layer: **Abstraction**. Depends on
  `Microsoft.Extensions.Options.DataAnnotations` and
  `Microsoft.Extensions.Configuration.Binder`.
- `<IsAotCandidate>true</IsAotCandidate>`. DataAnnotations validation
  uses reflection; the public wrapper methods are annotated with
  `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]` so the AOT
  analyzer propagates the requirement to callers rather than
  failing at the wrapper site. AOT-strict consumers can supply an
  `IKunConfigurationValidator<T>` instead and skip the
  reflection-based path entirely.

## Public Surface

| Type | Purpose |
|------|---------|
| `AddKunOptions<T>(section)` | Registers options of type `T`, binds to config section, validates DataAnnotations, and fails startup if validation fails. Section name is preserved in error messages. |
| `IKunConfigurationValidator<T>` | Reflection-free validator abstraction — consumers implement `Validate(string? name, T options)` and register via `services.AddSingleton<IKunConfigurationValidator<T>, …>()`. Errors are merged with DataAnnotations failures. |
| `[RequiredConfig]` | Options-aware `[Required]` — produces `'{Property}' is required but missing in configuration.` |
| `[UrlConfig]` | Validates absolute `Uri.TryCreate` success; null passes (pair with `[RequiredConfig]` when mandatory). |
| `[NonEmptyStringConfig]` | Rejects null, empty, or whitespace-only strings. |

## Wiring

```csharp
// Program.cs
services.AddKunOptions<AuthentikOptions>("Authentik");

// AuthentikOptions.cs
public sealed class AuthentikOptions
{
    [RequiredConfig]
    [UrlConfig]
    public string Authority { get; init; } = string.Empty;

    [RequiredConfig]
    [NonEmptyStringConfig]
    public string ClientId { get; init; } = string.Empty;

    [RequiredConfig]
    public string ClientSecret { get; init; } = string.Empty;
}
```

A missing `Authentik` section, a typoed URL, or an empty `ClientId`
all fail `host.RunAsync()` with an `OptionsValidationException` whose
messages name the section path.

## For AI Agents

- Do not call `ValidateDataAnnotations()` directly from AOT-strict
  consumer code — the analyzer warns on every call site. Use
  `AddKunOptions<T>` so the attributes flow through one annotated
  entry point.
- Custom attributes must inherit `ValidationAttribute` and override
  `FormatErrorMessage` with a message that includes the property
  name (the section prefix is prepended automatically by
  `KunOptionsValidator<T>`).
- `IOptionsMonitor<T>` 在变更时会重建并重新校验。`AddKunOptions<T>` 现在注册保留最后有效值的监视器：绑定或校验失败不替换旧对象，也不通知消费者，同时发出 `kun.configuration.reload.rejected` 指标与无配置值的告警。`ValidateOnStart` 仍拒绝首份无效的关键配置。
- 传入 `JsonTypeInfo<T>` 的重载会登记 `OptionsSchema<T>`；`Export()` 输出 JSON Schema 2020-12。Schema 表达 JSON 契约，DataAnnotations 与自定义语义校验仍在 validator 中执行。

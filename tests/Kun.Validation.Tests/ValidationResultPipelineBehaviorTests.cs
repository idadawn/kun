using FluentAssertions;
using Kun.Core.Cqrs;
using Kun.Core.Results;
using Kun.Mediator;
using Kun.Mediator.DependencyInjection;
using Kun.Validation.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kun.Validation.Tests;

// ---------------------------------------------------------------------------
// Domain types for Result-variant integration tests
// ---------------------------------------------------------------------------

internal sealed record CreateOrderCommand(string Sku, int Quantity) : IRequest<Result<string>>;

internal sealed class CreateOrderResultHandler : IRequestHandler<CreateOrderCommand, Result<string>>
{
    private readonly ResultInvocationTracker _tracker;

    public CreateOrderResultHandler(ResultInvocationTracker tracker) => _tracker = tracker;

    public ValueTask<Result<string>> Handle(CreateOrderCommand r, CancellationToken ct)
    {
        _tracker.WasInvoked = true;
        // Let the test control whether the handler itself returns success or failure.
        return ValueTask.FromResult(_tracker.ReturnFailure
            ? Result<string>.Fail(Error.NotFound("sku.notFound", "sku " + r.Sku + " missing"))
            : Result<string>.Ok("order:" + r.Sku));
    }
}

internal sealed class ResultInvocationTracker
{
    public bool WasInvoked { get; set; }
    public bool ReturnFailure { get; set; }
}

internal sealed class SkuRequiredValidator : IValidator<CreateOrderCommand>
{
    public ValueTask<ValidationResult> ValidateAsync(CreateOrderCommand instance, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(instance.Sku))
        {
            var errors = new[] { new ValidationError(nameof(instance.Sku), "sku.required", "SKU is required.") };
            return ValueTask.FromResult(ValidationResult.Failure(errors));
        }
        return ValueTask.FromResult(ValidationResult.Success);
    }
}

internal sealed class QuantityPositiveValidator : IValidator<CreateOrderCommand>
{
    public ValueTask<ValidationResult> ValidateAsync(CreateOrderCommand instance, CancellationToken ct = default)
    {
        if (instance.Quantity <= 0)
        {
            var errors = new[] { new ValidationError(nameof(instance.Quantity), "qty.positive", "Quantity must be > 0.") };
            return ValueTask.FromResult(ValidationResult.Failure(errors));
        }
        return ValueTask.FromResult(ValidationResult.Success);
    }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public sealed class ValidationResultPipelineBehaviorTests
{
    private static ServiceProvider BuildServices(Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new ResultInvocationTracker());

        services.AddKunMediator(b => b
            .AddValidationResultBehavior<CreateOrderCommand, string>()
            .AddRequestHandler<CreateOrderCommand, Result<string>, CreateOrderResultHandler>());

        extra?.Invoke(services);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task NoValidatorsRegistered_PassesThrough_ReturnsHandlerResult()
    {
        await using var sp = BuildServices();
        var sender = sp.GetRequiredService<ISender>();
        var tracker = sp.GetRequiredService<ResultInvocationTracker>();

        var result = await sender.Send(
            new CreateOrderCommand("sku-1", 3),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("order:sku-1");
        tracker.WasInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task ValidatorReturnsNoErrors_HandlerInvoked_ReturnsSuccess()
    {
        await using var sp = BuildServices(svc =>
            svc.AddValidator<CreateOrderCommand, SkuRequiredValidator>());

        var sender = sp.GetRequiredService<ISender>();
        var tracker = sp.GetRequiredService<ResultInvocationTracker>();

        var result = await sender.Send(
            new CreateOrderCommand("sku-2", 1),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        tracker.WasInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task ValidatorReturnsErrors_ReturnsResultFail_HandlerNotInvoked()
    {
        await using var sp = BuildServices(svc =>
            svc.AddValidator<CreateOrderCommand, SkuRequiredValidator>());

        var sender = sp.GetRequiredService<ISender>();
        var tracker = sp.GetRequiredService<ResultInvocationTracker>();

        var result = await sender.Send(
            new CreateOrderCommand(Sku: "", Quantity: 1),
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("request.invalid");
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Message.Should().Contain("SKU is required");
        tracker.WasInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task MultipleValidatorsEachReportErrors_AggregatesIntoSingleFailMessage()
    {
        await using var sp = BuildServices(svc =>
        {
            svc.AddValidator<CreateOrderCommand, SkuRequiredValidator>();
            svc.AddValidator<CreateOrderCommand, QuantityPositiveValidator>();
        });

        var sender = sp.GetRequiredService<ISender>();
        var tracker = sp.GetRequiredService<ResultInvocationTracker>();

        var result = await sender.Send(
            new CreateOrderCommand(Sku: "", Quantity: -5),
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("SKU is required");
        result.Error.Message.Should().Contain("Quantity must be > 0");
        result.Error.Message.Should().Contain(";"); // joined separator
        tracker.WasInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task AllValidatorsPass_HandlerReturnsResultFail_BehaviorPropagatesHandlerFailure()
    {
        await using var sp = BuildServices(svc =>
            svc.AddValidator<CreateOrderCommand, SkuRequiredValidator>());

        var tracker = sp.GetRequiredService<ResultInvocationTracker>();
        tracker.ReturnFailure = true;
        var sender = sp.GetRequiredService<ISender>();

        var result = await sender.Send(
            new CreateOrderCommand("sku-3", 1),
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        // Handler-produced error survives — behaviour must NOT overwrite it.
        result.Error.Code.Should().Be("sku.notFound");
        result.Error.Type.Should().Be(ErrorType.NotFound);
        tracker.WasInvoked.Should().BeTrue();
    }
}

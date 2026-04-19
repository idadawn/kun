using FluentAssertions;
using Kun.Core.Cqrs;
using Kun.Mediator;
using Kun.Mediator.DependencyInjection;
using Kun.Validation.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kun.Validation.Tests;

// ---------------------------------------------------------------------------
// Domain types for integration tests
// ---------------------------------------------------------------------------

internal sealed record CreateUserCommand2(string Email, int Age) : IRequest<string>;

internal sealed class CreateUserHandler2 : IRequestHandler<CreateUserCommand2, string>
{
    // Tracks whether the handler was invoked — injected via DI
    private readonly InvocationTracker _tracker;

    public CreateUserHandler2(InvocationTracker tracker) => _tracker = tracker;

    public ValueTask<string> Handle(CreateUserCommand2 r, CancellationToken ct)
    {
        _tracker.WasInvoked = true;
        return ValueTask.FromResult("ok:" + r.Email);
    }
}

/// <summary>Shared invocation flag injected as singleton so tests can observe handler calls.</summary>
internal sealed class InvocationTracker
{
    public bool WasInvoked { get; set; }
}

// Second validator — always rejects age > 120 — used for multi-validator aggregation test.
internal sealed class AgeRangeValidator : IValidator<CreateUserCommand2>
{
    public ValueTask<ValidationResult> ValidateAsync(CreateUserCommand2 instance, CancellationToken ct = default)
    {
        var errors = new List<ValidationError>();
        if (instance.Age > 120)
            errors.Add(new ValidationError(nameof(instance.Age), "age.tooHigh", "Age must be <= 120."));
        return ValueTask.FromResult(errors.Count == 0 ? ValidationResult.Success : ValidationResult.Failure(errors));
    }
}

// Validator that rejects empty email — used alongside AgeRangeValidator.
internal sealed class EmailRequiredValidator : IValidator<CreateUserCommand2>
{
    public ValueTask<ValidationResult> ValidateAsync(CreateUserCommand2 instance, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(instance.Email))
        {
            var errors = new[] { new ValidationError(nameof(instance.Email), "email.required", "Email is required.") };
            return ValueTask.FromResult(ValidationResult.Failure(errors));
        }
        return ValueTask.FromResult(ValidationResult.Success);
    }
}

// ---------------------------------------------------------------------------
// Integration tests
// ---------------------------------------------------------------------------

public sealed class ValidationPipelineBehaviorTests
{
    private static ServiceProvider BuildServices(Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        var tracker = new InvocationTracker();
        services.AddSingleton(tracker);

        services.AddKunMediator(b => b
            .AddValidationBehavior<CreateUserCommand2, string>()
            .AddRequestHandler<CreateUserCommand2, string, CreateUserHandler2>());

        extra?.Invoke(services);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Mediator_WithValidInput_InvokesHandler_ReturnsResponse()
    {
        await using var sp = BuildServices(svc =>
            svc.AddValidator<CreateUserCommand2, AgeRangeValidator>());

        var sender = sp.GetRequiredService<ISender>();
        var tracker = sp.GetRequiredService<InvocationTracker>();

        var response = await sender.Send(new CreateUserCommand2("user@example.com", 30),
            TestContext.Current.CancellationToken);

        response.Should().Be("ok:user@example.com");
        tracker.WasInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task Mediator_WithInvalidInput_ThrowsValidationException_HandlerNotInvoked()
    {
        await using var sp = BuildServices(svc =>
            svc.AddValidator<CreateUserCommand2, AgeRangeValidator>());

        var sender = sp.GetRequiredService<ISender>();
        var tracker = sp.GetRequiredService<InvocationTracker>();

        var act = async () => await sender.Send(new CreateUserCommand2("user@example.com", 200),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ValidationException>()
            .Where(ex => ex.Errors.Any(e => e.Code == "age.tooHigh"));

        tracker.WasInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task Mediator_WithMultipleValidators_AggregatesErrors()
    {
        // Register both validators — empty email triggers EmailRequiredValidator,
        // age > 120 triggers AgeRangeValidator; both errors should appear.
        await using var sp = BuildServices(svc =>
        {
            svc.AddValidator<CreateUserCommand2, EmailRequiredValidator>();
            svc.AddValidator<CreateUserCommand2, AgeRangeValidator>();
        });

        var sender = sp.GetRequiredService<ISender>();
        var tracker = sp.GetRequiredService<InvocationTracker>();

        var act = async () => await sender.Send(new CreateUserCommand2("", 200),
            TestContext.Current.CancellationToken);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().Contain(e => e.Code == "email.required");
        ex.Which.Errors.Should().Contain(e => e.Code == "age.tooHigh");

        tracker.WasInvoked.Should().BeFalse();
    }
}

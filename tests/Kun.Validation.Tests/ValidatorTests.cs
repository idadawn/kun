using FluentAssertions;
using Xunit;

namespace Kun.Validation.Tests;

// ---------------------------------------------------------------------------
// Shared domain types
// ---------------------------------------------------------------------------

internal sealed record CreateUserCommand(string Email, int Age);

internal sealed class CreateUserValidator : IValidator<CreateUserCommand>
{
    public ValueTask<ValidationResult> ValidateAsync(CreateUserCommand instance, CancellationToken ct = default)
    {
        var errors = new List<ValidationError>();
        if (string.IsNullOrWhiteSpace(instance.Email))
            errors.Add(new ValidationError(nameof(instance.Email), "email.required", "Email is required."));
        else if (!instance.Email.Contains('@'))
            errors.Add(new ValidationError(nameof(instance.Email), "email.format", "Email must contain '@'."));
        if (instance.Age < 0)
            errors.Add(new ValidationError(nameof(instance.Age), "age.negative", "Age must be >= 0."));
        return ValueTask.FromResult(errors.Count == 0 ? ValidationResult.Success : ValidationResult.Failure(errors));
    }
}

// ---------------------------------------------------------------------------
// ValidationResult unit tests
// ---------------------------------------------------------------------------

public sealed class ValidationResultTests
{
    [Fact]
    public void ValidationResult_Success_IsValidTrueAndErrorsEmpty()
    {
        var result = ValidationResult.Success;

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidationResult_Failure_WithEmptyErrors_Throws()
    {
        var act = () => ValidationResult.Failure(Array.Empty<ValidationError>());

        act.Should().Throw<ArgumentException>()
            .WithMessage("*at least one error*");
    }

    [Fact]
    public void ValidationResult_Failure_WithErrors_IsValidFalseAndMatchesInput()
    {
        var errors = new[] { new ValidationError("Email", "email.required", "Email is required.") };

        var result = ValidationResult.Failure(errors);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Code.Should().Be("email.required");
    }
}

// ---------------------------------------------------------------------------
// IValidator unit tests
// ---------------------------------------------------------------------------

public sealed class ValidatorTests
{
    private readonly CreateUserValidator _validator = new();

    [Fact]
    public async Task Validator_ValidInput_ReturnsSuccess()
    {
        var result = await _validator.ValidateAsync(new CreateUserCommand("user@example.com", 25), TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Validator_EmptyEmail_ReturnsFailureWithEmailRequired()
    {
        var result = await _validator.ValidateAsync(new CreateUserCommand("", 25), TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "email.required");
    }

    [Fact]
    public async Task Validator_MissingAtSign_ReturnsFailureWithEmailFormat()
    {
        var result = await _validator.ValidateAsync(new CreateUserCommand("notanemail", 25), TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "email.format");
    }

    [Fact]
    public async Task Validator_NegativeAge_ReturnsFailureWithAgeNegative()
    {
        var result = await _validator.ValidateAsync(new CreateUserCommand("user@example.com", -1), TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "age.negative");
    }

    [Fact]
    public async Task Validator_MultipleFaults_AccumulatesAllErrors()
    {
        var result = await _validator.ValidateAsync(new CreateUserCommand("", -5), TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().Contain(e => e.Code == "email.required");
        result.Errors.Should().Contain(e => e.Code == "age.negative");
    }
}

// ---------------------------------------------------------------------------
// ValidationException unit tests
// ---------------------------------------------------------------------------

public sealed class ValidationExceptionTests
{
    [Fact]
    public void ValidationException_CarriesErrors()
    {
        var errors = new[]
        {
            new ValidationError("Email", "email.required", "Email is required."),
            new ValidationError("Age", "age.negative", "Age must be >= 0.")
        };

        var ex = new ValidationException(errors);

        ex.Errors.Should().HaveCount(2);
        ex.Errors[0].Code.Should().Be("email.required");
        ex.Errors[1].Code.Should().Be("age.negative");
        ex.Message.Should().Contain("2 error(s)");
    }
}

using FluentAssertions;
using Kun.Core.Results;
using Xunit;

namespace Kun.Core.Tests.Results;

public class ResultTests
{
    // ── Result (non-generic) ────────────────────────────────────────────────

    [Fact]
    public void Ok_IsSuccessTrueAndErrorIsNone()
    {
        // Act
        var result = Result.Ok();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void Fail_IsSuccessFalseAndErrorMatches()
    {
        // Arrange
        var error = Error.Failure("test.fail", "Something went wrong");

        // Act
        var result = Result.Fail(error);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void Fail_WithErrorNone_Throws()
    {
        // Act
        var act = () => Result.Fail(Error.None);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Result<T> ───────────────────────────────────────────────────────────

    [Fact]
    public void ResultT_Ok_ValueAccessibleAndIsSuccess()
    {
        // Act
        var result = Result<int>.Ok(42);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void ResultT_Fail_IsSuccessFalseAndErrorMatches()
    {
        // Arrange
        var error = Error.NotFound("item.notFound", "Item not found");

        // Act
        var result = Result<int>.Fail(error);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void ResultT_Value_OnFailure_Throws()
    {
        // Arrange
        var result = Result<int>.Fail(Error.Failure("x", "y"));

        // Act
        var act = () => result.Value;

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ResultT_ImplicitConversion_FromValue_IsSuccess()
    {
        // Act
        Result<int> result = 42;

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void ResultT_ImplicitConversion_FromError_IsFailure()
    {
        // Arrange
        var error = Error.NotFound("x", "y");

        // Act
        Result<int> result = error;

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void EnsureSuccess_OnSuccess_ReturnsValue()
    {
        // Arrange
        var result = Result<string>.Ok("hello");

        // Act
        var value = result.EnsureSuccess();

        // Assert
        value.Should().Be("hello");
    }

    [Fact]
    public void EnsureSuccess_OnFailure_ThrowsResultFailureException_WithMatchingError()
    {
        // Arrange
        var error = Error.Conflict("order.conflict", "Duplicate order");
        var result = Result<string>.Fail(error);

        // Act
        var act = () => result.EnsureSuccess();

        // Assert
        act.Should().Throw<ResultFailureException>()
            .Which.Error.Should().Be(error);
    }

    // ── Error factory methods ───────────────────────────────────────────────

    [Fact]
    public void Error_FactoryMethods_SetCorrectType()
    {
        // Act & Assert
        Error.Failure("c", "m").Type.Should().Be(ErrorType.Failure);
        Error.Validation("c", "m").Type.Should().Be(ErrorType.Validation);
        Error.NotFound("c", "m").Type.Should().Be(ErrorType.NotFound);
        Error.Conflict("c", "m").Type.Should().Be(ErrorType.Conflict);
        Error.Unauthorized("c", "m").Type.Should().Be(ErrorType.Unauthorized);
        Error.Forbidden("c", "m").Type.Should().Be(ErrorType.Forbidden);
        Error.Unavailable("c", "m").Type.Should().Be(ErrorType.Unavailable);
    }
}

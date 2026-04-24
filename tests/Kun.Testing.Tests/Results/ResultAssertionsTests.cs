using FluentAssertions;
using Kun.Core.Results;
using Kun.Testing.Results;
using Xunit;

namespace Kun.Testing.Tests.Results;

public class ResultAssertionsTests
{
    private static readonly Error NotFound = Error.NotFound("user.notFound", "User not found");

    // ── Result (non-generic) ────────────────────────────────────────────────

    [Fact]
    public void ShouldBeSuccess_OnSuccess_DoesNotThrow()
    {
        var act = () => Result.Ok().ShouldBeSuccess();
        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldBeSuccess_OnFailure_ThrowsWithErrorDetails()
    {
        var act = () => Result.Fail(NotFound).ShouldBeSuccess();
        act.Should().Throw<KunAssertionException>()
            .WithMessage("*ShouldBeSuccess*expected success*failure*user.notFound*NotFound*");
    }

    [Fact]
    public void ShouldBeFailure_OnFailure_ReturnsErrorForChaining()
    {
        var err = Result.Fail(NotFound).ShouldBeFailure();
        err.Should().Be(NotFound);
    }

    [Fact]
    public void ShouldBeFailure_OnSuccess_Throws()
    {
        var act = () => Result.Ok().ShouldBeFailure();
        act.Should().Throw<KunAssertionException>()
            .WithMessage("*ShouldBeFailure*expected failure*saw success*");
    }

    [Fact]
    public void ShouldHaveErrorCode_OnMatchingFailure_ReturnsError()
    {
        var err = Result.Fail(NotFound).ShouldHaveErrorCode("user.notFound");
        err.Code.Should().Be("user.notFound");
    }

    [Fact]
    public void ShouldHaveErrorCode_OnMismatchedCode_ThrowsWithExpectedAndActual()
    {
        var act = () => Result.Fail(NotFound).ShouldHaveErrorCode("order.notFound");
        act.Should().Throw<KunAssertionException>()
            .WithMessage("*ShouldHaveErrorCode*expected*order.notFound*saw*user.notFound*");
    }

    [Fact]
    public void ShouldHaveErrorCode_OnSuccess_Throws()
    {
        var act = () => Result.Ok().ShouldHaveErrorCode("any.code");
        act.Should().Throw<KunAssertionException>()
            .WithMessage("*ShouldHaveErrorCode*expected failure*any.code*saw success*");
    }

    [Fact]
    public void ShouldHaveErrorCode_NullCode_Throws()
    {
        var act = () => Result.Fail(NotFound).ShouldHaveErrorCode(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ShouldHaveErrorType_OnMatch_ReturnsError()
    {
        var err = Result.Fail(NotFound).ShouldHaveErrorType(ErrorType.NotFound);
        err.Should().Be(NotFound);
    }

    [Fact]
    public void ShouldHaveErrorType_OnMismatch_ThrowsWithBothTypes()
    {
        var act = () => Result.Fail(NotFound).ShouldHaveErrorType(ErrorType.Conflict);
        act.Should().Throw<KunAssertionException>()
            .WithMessage("*ShouldHaveErrorType*expected*Conflict*saw*NotFound*");
    }

    // ── Result<T> ───────────────────────────────────────────────────────────

    [Fact]
    public void ShouldBeSuccess_T_OnSuccess_ReturnsValue()
    {
        var value = Result<int>.Ok(42).ShouldBeSuccess();
        value.Should().Be(42);
    }

    [Fact]
    public void ShouldBeSuccess_T_OnFailure_ThrowsWithTypeNameAndErrorDetails()
    {
        var act = () => Result<int>.Fail(NotFound).ShouldBeSuccess();
        act.Should().Throw<KunAssertionException>()
            .WithMessage("*ShouldBeSuccess<Int32>*user.notFound*");
    }

    [Fact]
    public void ShouldBeFailure_T_OnFailure_ReturnsError()
    {
        var err = Result<int>.Fail(NotFound).ShouldBeFailure();
        err.Should().Be(NotFound);
    }

    [Fact]
    public void ShouldBeFailure_T_OnSuccess_ThrowsWithValueInMessage()
    {
        var act = () => Result<int>.Ok(99).ShouldBeFailure();
        act.Should().Throw<KunAssertionException>()
            .WithMessage("*ShouldBeFailure<Int32>*saw success*99*");
    }

    [Fact]
    public void ShouldHaveErrorCode_T_OnMatch_ReturnsError()
    {
        var err = Result<string>.Fail(NotFound).ShouldHaveErrorCode("user.notFound");
        err.Should().Be(NotFound);
    }

    [Fact]
    public void ShouldHaveErrorCode_T_OnMismatch_Throws()
    {
        var act = () => Result<string>.Fail(NotFound).ShouldHaveErrorCode("order.notFound");
        act.Should().Throw<KunAssertionException>()
            .WithMessage("*ShouldHaveErrorCode<String>*order.notFound*user.notFound*");
    }

    [Fact]
    public void ShouldHaveErrorType_T_OnMatch_ReturnsError()
    {
        var err = Result<int>.Fail(NotFound).ShouldHaveErrorType(ErrorType.NotFound);
        err.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public void ShouldHaveErrorType_T_OnSuccess_ThrowsWithValueInMessage()
    {
        var act = () => Result<int>.Ok(7).ShouldHaveErrorType(ErrorType.Validation);
        act.Should().Throw<KunAssertionException>()
            .WithMessage("*ShouldHaveErrorType<Int32>*Validation*saw success*7*");
    }
}

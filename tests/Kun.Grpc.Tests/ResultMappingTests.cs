using FluentAssertions;
using Grpc.Core;
using Kun.Core.Results;
using Kun.Grpc.Interceptors;
using Xunit;

namespace Kun.Grpc.Tests;

/// <summary>
/// Tests that each <see cref="ErrorType"/> maps to the correct gRPC <see cref="StatusCode"/>
/// via <see cref="KunResultToRpcInterceptor.MapErrorType"/> and
/// <see cref="KunResultToRpcInterceptor.ToRpcException"/>.
/// </summary>
public sealed class ResultMappingTests
{
    [Theory]
    [InlineData(ErrorType.Validation,   StatusCode.InvalidArgument)]
    [InlineData(ErrorType.NotFound,     StatusCode.NotFound)]
    [InlineData(ErrorType.Conflict,     StatusCode.FailedPrecondition)]
    [InlineData(ErrorType.Unauthorized, StatusCode.Unauthenticated)]
    [InlineData(ErrorType.Forbidden,    StatusCode.PermissionDenied)]
    [InlineData(ErrorType.Unavailable,  StatusCode.Unavailable)]
    [InlineData(ErrorType.Failure,      StatusCode.Internal)]
    public void MapErrorType_MapsErrorTypeToCorrectStatusCode(ErrorType errorType, StatusCode expectedCode)
    {
        // Act
        var statusCode = KunResultToRpcInterceptor.MapErrorType(errorType);

        // Assert
        statusCode.Should().Be(expectedCode);
    }

    [Fact]
    public void ToRpcException_SetsStatusCodeFromErrorType()
    {
        // Arrange
        var error = Error.NotFound("order.notFound", "Order not found");

        // Act
        var ex = KunResultToRpcInterceptor.ToRpcException(error);

        // Assert
        ex.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact]
    public void ToRpcException_SetsStatusMessageFromErrorMessage()
    {
        // Arrange
        var error = Error.Validation("name.required", "Name is required");

        // Act
        var ex = KunResultToRpcInterceptor.ToRpcException(error);

        // Assert
        ex.Status.Detail.Should().Be("Name is required");
    }

    [Fact]
    public void ToRpcException_IncludesErrorCodeInTrailers()
    {
        // Arrange
        var error = Error.Conflict("order.duplicate", "Order already exists");

        // Act
        var ex = KunResultToRpcInterceptor.ToRpcException(error);

        // Assert
        var codeTrailer = ex.Trailers.FirstOrDefault(e => e.Key == "kun-error-code");
        codeTrailer.Should().NotBeNull();
        codeTrailer!.Value.Should().Be("order.duplicate");
    }

    [Fact]
    public void ToRpcException_GenericFailure_MapsToInternal()
    {
        // Arrange
        var error = Error.Failure("system.error", "Unexpected error");

        // Act
        var ex = KunResultToRpcInterceptor.ToRpcException(error);

        // Assert
        ex.StatusCode.Should().Be(StatusCode.Internal);
    }

    [Fact]
    public void ToRpcException_Unauthorized_MapsToUnauthenticated()
    {
        // Arrange
        var error = Error.Unauthorized("auth.required", "Authentication required");

        // Act
        var ex = KunResultToRpcInterceptor.ToRpcException(error);

        // Assert
        ex.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    [Fact]
    public void ToRpcException_Forbidden_MapsToPermissionDenied()
    {
        // Arrange
        var error = Error.Forbidden("access.denied", "Access denied");

        // Act
        var ex = KunResultToRpcInterceptor.ToRpcException(error);

        // Assert
        ex.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }
}

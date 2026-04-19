using FluentAssertions;
using Kun.AspNetCore.Mapping;
using Kun.Core.Results;
using Xunit;

namespace Kun.AspNetCore.Tests;

/// <summary>
/// Tests that each <see cref="ErrorType"/> maps to the correct HTTP status code
/// via <see cref="ErrorToProblemDetailsExtensions.ToProblemDetails"/>.
/// </summary>
public sealed class ErrorToProblemDetailsTests
{
    [Theory]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.Unavailable, 503)]
    [InlineData(ErrorType.Failure, 500)]
    public void ToProblemDetails_MapsErrorTypeToCorrectStatus(ErrorType type, int expectedStatus)
    {
        // Arrange
        var error = new Error("test.code", "Test message", type);

        // Act
        var pd = error.ToProblemDetails();

        // Assert
        pd.Status.Should().Be(expectedStatus);
    }

    [Fact]
    public void ToProblemDetails_PopulatesCodeExtension()
    {
        // Arrange
        var error = Error.NotFound("order.notFound", "Order not found");

        // Act
        var pd = error.ToProblemDetails();

        // Assert
        pd.Extensions.Should().ContainKey("code");
        pd.Extensions["code"].Should().Be("order.notFound");
    }

    [Fact]
    public void ToProblemDetails_PopulatesDetail()
    {
        // Arrange
        var error = Error.Validation("name.required", "Name is required");

        // Act
        var pd = error.ToProblemDetails();

        // Assert
        pd.Detail.Should().Be("Name is required");
    }

    [Fact]
    public void ToProblemDetails_ValidationError_Returns400WithBadRequestTitle()
    {
        // Arrange
        var error = Error.Validation("field.invalid", "Field is invalid");

        // Act
        var pd = error.ToProblemDetails();

        // Assert
        pd.Status.Should().Be(400);
        pd.Title.Should().Be("Bad Request");
    }

    [Fact]
    public void ToProblemDetails_UnavailableError_Returns503()
    {
        // Arrange
        var error = Error.Unavailable("db.unavailable", "Database unavailable");

        // Act
        var pd = error.ToProblemDetails();

        // Assert
        pd.Status.Should().Be(503);
        pd.Title.Should().Be("Service Unavailable");
    }
}

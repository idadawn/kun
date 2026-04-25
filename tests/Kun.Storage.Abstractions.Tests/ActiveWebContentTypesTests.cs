using FluentAssertions;
using Xunit;

namespace Kun.Storage.Abstractions.Tests;

/// <summary>
/// Guards the M1 invariant: <see cref="ActiveWebContentTypes.Value"/> contains exactly
/// the four canonical active web content types. Any drift breaks the security contract
/// S3 / Local implementations rely on.
/// </summary>
public sealed class ActiveWebContentTypesTests
{
    private static readonly string[] CanonicalTypes =
    [
        "text/html",
        "image/svg+xml",
        "application/xhtml+xml",
        "application/xml",
    ];

    [Fact]
    public void Value_ContainsExactlyFourCanonicalTypes()
    {
        ActiveWebContentTypes.Value.Should().BeEquivalentTo(CanonicalTypes);
    }

    [Fact]
    public void Value_HasFourEntries()
    {
        ActiveWebContentTypes.Value.Count.Should().Be(4);
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("application/xhtml+xml")]
    [InlineData("application/xml")]
    public void Contains_TrueForCanonical(string contentType)
    {
        ActiveWebContentTypes.Contains(contentType).Should().BeTrue();
    }

    [Theory]
    [InlineData("TEXT/HTML")]
    [InlineData("Image/SVG+XML")]
    [InlineData("APPLICATION/xhtml+xml")]
    public void Contains_IsCaseInsensitive(string contentType)
    {
        ActiveWebContentTypes.Contains(contentType).Should().BeTrue();
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("application/pdf")]
    [InlineData("text/plain")]
    [InlineData("application/json")]
    [InlineData("")]
    [InlineData(null)]
    public void Contains_FalseForNonActiveWebTypes(string? contentType)
    {
        ActiveWebContentTypes.Contains(contentType).Should().BeFalse();
    }
}

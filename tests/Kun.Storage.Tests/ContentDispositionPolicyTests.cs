using FluentAssertions;
using Kun.Storage.Implementations;
using Xunit;

namespace Kun.Storage.Tests;

public sealed class ContentDispositionPolicyTests
{
    [Theory]
    [InlineData("text/html")]
    [InlineData("text/xml")]
    [InlineData("application/xhtml+xml")]
    [InlineData("application/xml")]
    [InlineData("image/svg+xml")]
    [InlineData("application/x-shockwave-flash")]
    [InlineData("text/HTML")]                    // case-insensitive
    [InlineData("text/html; charset=utf-8")]     // strips parameters
    public void ActiveWebTypes_RequireAttachment(string contentType)
    {
        ContentDispositionPolicy.RequiresAttachment(contentType).Should().BeTrue();
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/pdf")]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("application/octet-stream")]
    [InlineData("")]
    [InlineData(null)]
    public void SafeTypes_DoNotRequireAttachment(string? contentType)
    {
        ContentDispositionPolicy.RequiresAttachment(contentType).Should().BeFalse();
    }

    [Fact]
    public void BuildHeaderValue_ReturnsAttachmentWithEncodedFilename_ForActiveWebType()
    {
        var header = ContentDispositionPolicy.BuildHeaderValue("uploads/evil page.html", "text/html");

        header.Should().Be("attachment; filename=\"evil%20page.html\"");
    }

    [Fact]
    public void BuildHeaderValue_UsesLastPathSegment()
    {
        var header = ContentDispositionPolicy.BuildHeaderValue("tenant-a/2026/04/report.xml", "text/xml");
        header.Should().Be("attachment; filename=\"report.xml\"");
    }

    [Fact]
    public void BuildHeaderValue_ReturnsNull_ForSafeContentType()
    {
        ContentDispositionPolicy.BuildHeaderValue("doc.pdf", "application/pdf").Should().BeNull();
    }
}

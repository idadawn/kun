using FluentAssertions;
using Kun.Storage.Abstractions;
using Xunit;

namespace Kun.Storage.Tests;

public sealed class ContentDispositionPolicyTests
{
    [Theory]
    [InlineData("text/html")]
    [InlineData("TEXT/HTML")]
    [InlineData("application/xhtml+xml")]
    [InlineData("image/svg+xml")]
    [InlineData("application/xml")]
    [InlineData("text/xml")]
    [InlineData("application/javascript")]
    [InlineData("text/javascript")]
    [InlineData("application/ecmascript")]
    [InlineData("application/wasm")]
    [InlineData("text/html; charset=utf-8")]
    public void IsUnsafeForInlineDisplay_True_For_ActiveWeb_Types(string mime)
    {
        ContentDispositionPolicy.IsUnsafeForInlineDisplay(mime).Should().BeTrue();
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/json")]
    [InlineData("image/png")]
    [InlineData("application/pdf")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsUnsafeForInlineDisplay_False_For_Safe_Or_Empty(string? mime)
    {
        ContentDispositionPolicy.IsUnsafeForInlineDisplay(mime).Should().BeFalse();
    }

    [Fact]
    public void EnforceAttachment_Passes_Through_When_Mime_Is_Safe()
    {
        ContentDispositionPolicy.EnforceAttachment("image/png", "inline", "pic.png")
            .Should().Be("inline");
    }

    [Fact]
    public void EnforceAttachment_Replaces_Inline_Disposition_For_Unsafe_Mime()
    {
        var result = ContentDispositionPolicy.EnforceAttachment("text/html", "inline", "page.html");
        result.Should().NotBeNull();
        result!.Should().StartWith("attachment");
        result.Should().Contain("page.html");
    }

    [Fact]
    public void EnforceAttachment_Keeps_Existing_Attachment_Disposition()
    {
        var result = ContentDispositionPolicy.EnforceAttachment(
            "text/html", "attachment; filename=\"custom.html\"", "page.html");
        result.Should().Be("attachment; filename=\"custom.html\"");
    }

    [Fact]
    public void EnforceAttachment_Uses_Bare_Attachment_When_FileName_Missing()
    {
        ContentDispositionPolicy.EnforceAttachment("text/html", null, null)
            .Should().Be("attachment");
    }

    [Fact]
    public void BuildAttachmentDisposition_Rfc5987_Encodes_NonAscii_Filenames()
    {
        var result = ContentDispositionPolicy.BuildAttachmentDisposition("日志.html");
        result.Should().StartWith("attachment");
        result.Should().Contain("filename*=UTF-8''");
        result.Should().Contain("%E6%97%A5%E5%BF%97.html");
    }

    [Fact]
    public void BuildAttachmentDisposition_Returns_Bare_When_FileName_Empty()
    {
        ContentDispositionPolicy.BuildAttachmentDisposition("").Should().Be("attachment");
        ContentDispositionPolicy.BuildAttachmentDisposition(null).Should().Be("attachment");
        ContentDispositionPolicy.BuildAttachmentDisposition("   ").Should().Be("attachment");
    }

    [Fact]
    public void BuildAttachmentDisposition_Escapes_Double_Quotes_In_Ascii_Fallback()
    {
        var result = ContentDispositionPolicy.BuildAttachmentDisposition("a\"b.html");
        result.Should().Contain("filename=\"a_b.html\"");
    }
}

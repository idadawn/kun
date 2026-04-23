using FluentAssertions;
using Kun.Configuration.Attributes;
using Xunit;

namespace Kun.Configuration.Tests;

public sealed class AttributeTests
{
    [Fact]
    public void RequiredConfig_ErrorMessage_NamesTheProperty()
    {
        var attr = new RequiredConfigAttribute();
        attr.FormatErrorMessage("ClientId")
            .Should().Be("'ClientId' is required but missing in configuration.");
    }

    [Theory]
    [InlineData("https://idp.test", true)]
    [InlineData("http://internal:8080/path?x=1", true)]
    [InlineData(null, true)]                    // null is acceptable unless paired with [RequiredConfig]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("not-a-url", false)]
    public void UrlConfig_Validates(string? value, bool expectedValid)
    {
        var attr = new UrlConfigAttribute();
        attr.IsValid(value).Should().Be(expectedValid);
    }

    [Theory]
    [InlineData("value", true)]
    [InlineData(null, true)]                    // null is acceptable unless paired with [RequiredConfig]
    [InlineData("", false)]
    [InlineData("   \t  ", false)]
    public void NonEmptyStringConfig_Validates(string? value, bool expectedValid)
    {
        var attr = new NonEmptyStringConfigAttribute();
        attr.IsValid(value).Should().Be(expectedValid);
    }

    [Fact]
    public void UrlConfig_ErrorMessage_IsDescriptive()
    {
        new UrlConfigAttribute().FormatErrorMessage("Authority")
            .Should().Be("'Authority' must be a valid absolute URL.");
    }

    [Fact]
    public void NonEmptyStringConfig_ErrorMessage_IsDescriptive()
    {
        new NonEmptyStringConfigAttribute().FormatErrorMessage("ClientId")
            .Should().Be("'ClientId' must be a non-empty string.");
    }
}

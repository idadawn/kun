using FluentAssertions;
using Kun.EventBus.RabbitMQ;
using Xunit;

namespace Kun.IntegrationTests.EventBus;

public sealed class SanitizeUriTests
{
    [Theory]
    [InlineData("amqp://neuro:s3cr3t@host:5672/", "amqp://neuro:***@host:5672/")]
    [InlineData("amqps://u:p@h/vhost", "amqps://u:***@h/vhost")]
    [InlineData("amqp://host:5672/", "amqp://host:5672/")] // no credentials — untouched
    [InlineData("amqp://user@host/", "amqp://user@host/")] // no password — untouched
    public void SanitizeUri_MasksOnlyThePassword(string input, string expected)
        => RabbitMqConnectionProvider.SanitizeUri(input).Should().Be(expected);
}

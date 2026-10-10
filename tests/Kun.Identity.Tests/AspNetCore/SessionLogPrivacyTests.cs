using System.Security.Cryptography;
using System.Text;
using Kun.Identity.Abstractions;
using Kun.Identity.Abstractions.Session;
using Kun.Identity.AspNetCore;
using Kun.Identity.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kun.Identity.Tests.AspNetCore;

public sealed class SessionLogPrivacyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StoreFailure_LogsHashOnly_EvenWhenBackendExceptionContainsRawSid(bool renewal)
    {
        const string sid = "private-browser-session-do-not-log";
        var logger = new CaptureLogger(); var nextCalled = false;
        var middleware = new KunCookieAuthMiddleware(context => { nextCalled = true; Assert.False(context.User.Identity?.IsAuthenticated == true); return Task.CompletedTask; },
            new FailingStore(renewal), Options.Create(new KunCookieAuthOptions()), TimeProvider.System, logger);
        var request = new DefaultHttpContext(); request.Request.Method = HttpMethods.Get; request.Request.Headers.Cookie = "kun.sid=" + sid;
        await middleware.InvokeAsync(request);
        Assert.True(nextCalled);
        Assert.Single(logger.Messages);
        Assert.DoesNotContain(sid, logger.Messages[0]);
        Assert.Contains(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid))), logger.Messages[0]);
        Assert.Null(logger.Exception);
    }

    private sealed class CaptureLogger : ILogger<KunCookieAuthMiddleware>
    {
        internal List<string> Messages { get; } = [];
        internal Exception? Exception { get; private set; }
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { Messages.Add(formatter(state, exception)); Exception = exception; }
    }
    private sealed class FailingStore(bool renewal) : ISessionRegistry
    {
        public Task<SessionData?> GetAsync(string sessionId, CancellationToken cancellationToken = default) => renewal
            ? Task.FromResult<SessionData?>(new SessionData("user-a", null, null, null, [], "access", null, DateTimeOffset.UtcNow.AddMinutes(1)))
            : throw new InvalidOperationException("backend failed for " + sessionId);
        public Task<bool> RenewAsync(string sessionId, TimeSpan ttl, CancellationToken cancellationToken = default) => throw new InvalidOperationException("backend failed for " + sessionId);
        public Task SetAsync(string sessionId, SessionData data, TimeSpan ttl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> RevokeUserAsync(string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> ApplyLogoutAsync(OidcLogoutToken logout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryUseNonceAsync(string purpose, string nonce, TimeSpan ttl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

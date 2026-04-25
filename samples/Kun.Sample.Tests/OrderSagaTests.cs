extern alias OrderApp;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Grpc.Core;
using Kun.Sample.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
// The gRPC client code (CatalogService.CatalogServiceClient) is generated into the Order
// assembly (GrpcServices="Client"), not the Catalog assembly (GrpcServices="Server").
using OrderApp::Kun.Sample.Catalog.Grpc;
using Xunit;

namespace Kun.Sample.Tests;

// ── Limitations documented ────────────────────────────────────────────────────
// The OrderCreationSaga's first step (ReserveInventoryStep) makes a live gRPC
// call to the Catalog service. Running both WebApplicationFactory<Catalog> and
// WebApplicationFactory<Order> simultaneously in the same process is feasible but
// requires wiring the Order factory's gRPC client address to the in-process Catalog
// test server, which requires HTTP/2 h2c support in the test host and cross-process
// port discovery.
//
// Chosen approach: replace CatalogService.CatalogServiceClient with a fake backed
// by a no-op CallInvoker so there is no actual network call. This keeps tests fast,
// deterministic, and free of inter-service networking while still exercising the
// full saga orchestration and DB persistence paths.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A <see cref="CallInvoker"/> that never makes network calls.
/// Used only as a base-class constructor argument — the overridden methods are never reached.
/// </summary>
internal sealed class NullCallInvoker : CallInvoker
{
    internal static readonly NullCallInvoker Instance = new();

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        => throw new NotSupportedException("NullCallInvoker is a stub.");

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        => throw new NotSupportedException("NullCallInvoker is a stub.");

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        => throw new NotSupportedException();

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options)
        => throw new NotSupportedException();

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options)
        => throw new NotSupportedException();
}

/// <summary>
/// Fake gRPC client that returns configurable in-memory responses.
/// </summary>
internal sealed class FakeCatalogServiceClient : CatalogService.CatalogServiceClient
{
    private readonly bool _reserveSuccess;
    private readonly string _failureReason;

    internal FakeCatalogServiceClient(bool reserveSuccess = true, string failureReason = "")
        : base(NullCallInvoker.Instance)
    {
        _reserveSuccess = reserveSuccess;
        _failureReason = failureReason;
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<ReserveStockReply> ReserveStockAsync(
        ReserveStockRequest request,
        CallOptions options)
    {
        var reply = new ReserveStockReply
        {
            Success = _reserveSuccess,
            FailureReason = _failureReason,
        };
        return new AsyncUnaryCall<ReserveStockReply>(
            Task.FromResult(reply),
            Task.FromResult(Metadata.Empty),
            () => Status.DefaultSuccess,
            () => Metadata.Empty,
            () => { });
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<ReserveStockReply> ReserveStockAsync(
        ReserveStockRequest request,
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
        => ReserveStockAsync(request, new CallOptions(headers, deadline, cancellationToken));
}

/// <summary>
/// Shared fixture: one Postgres container + one Order WebApplicationFactory for all saga tests.
/// </summary>
public sealed class OrderWebFactory : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private WebApplicationFactory<OrderApp::Program>? _factory;

    /// <summary>Gets the configured HttpClient for the Order service.</summary>
    public HttpClient Client { get; private set; } = null!;

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        await _postgres.InitializeAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__OrderDb", _postgres.ConnectionString);
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", "true");
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "");

        var cs = _postgres.ConnectionString;

        _factory = new WebApplicationFactory<OrderApp::Program>()
            .WithWebHostBuilder(builder =>
            {
                // Development env switches on DeveloperExceptionPage so the test
                // sees the real exception body when the handler throws (previously
                // ProblemDetails swallowed it to a generic 500).
                builder.UseEnvironment("Development");

                builder.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:OrderDb"] = cs,
                        ["OTEL_SDK_DISABLED"] = "true",
                    });
                });
                builder.UseSetting("ConnectionStrings:OrderDb", cs);

                builder.ConfigureTestServices(services =>
                {
                    // Remove ALL registrations for the typed gRPC client and replace with fake.
                    var toRemove = services
                        .Where(d => d.ServiceType == typeof(CatalogService.CatalogServiceClient))
                        .ToList();
                    foreach (var d in toRemove)
                        services.Remove(d);

                    services.AddSingleton<CatalogService.CatalogServiceClient>(
                        new FakeCatalogServiceClient(reserveSuccess: true));

                    // KunSagaBuilder registers OrderCreationSaga as singleton but the steps are
                    // registered as scoped — this triggers DI scope validation in Development.
                    // Re-register the saga and all its steps as transient to satisfy the validator.
                    // At runtime the saga orchestrator resolves steps from a scoped IServiceProvider
                    // (not from the singleton constructor), so this change is safe.
                    ReRegisterAsTransient<OrderApp::Kun.Sample.Order.Application.Sagas.OrderCreationSaga>(services);
                    ReRegisterAsTransient<OrderApp::Kun.Sample.Order.Application.Sagas.Steps.ReserveInventoryStep>(services);
                    ReRegisterAsTransient<OrderApp::Kun.Sample.Order.Application.Sagas.Steps.ProcessPaymentStep>(services);
                    ReRegisterAsTransient<OrderApp::Kun.Sample.Order.Application.Sagas.Steps.ShipOrderStep>(services);
                    ReRegisterAsTransient<OrderApp::Kun.Sample.Order.Application.Sagas.Steps.NotifyCustomerStep>(services);

                    static void ReRegisterAsTransient<T>(IServiceCollection svc) where T : class
                    {
                        var existing = svc.Where(d => d.ServiceType == typeof(T)).ToList();
                        foreach (var d in existing) svc.Remove(d);
                        svc.AddTransient<T>();
                    }
                });

                builder.ConfigureLogging(lb =>
                {
                    lb.ClearProviders();
                    lb.AddProvider(CapturingLoggerProvider.Instance);
                    lb.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);
                });
            });

        Client = _factory.CreateClient();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
        Environment.SetEnvironmentVariable("ConnectionStrings__OrderDb", null);
    }
}

/// <summary>
/// Shared fixture: Order WebApplicationFactory wired with a fake Catalog gRPC client
/// that always returns <c>Success=false</c> so the ReserveInventoryStep fails immediately.
/// </summary>
public sealed class OrderWebFactoryWithInventoryFailure : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private WebApplicationFactory<OrderApp::Program>? _factory;

    /// <summary>Gets the configured HttpClient for the Order service.</summary>
    public HttpClient Client { get; private set; } = null!;

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        await _postgres.InitializeAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__OrderDb", _postgres.ConnectionString);
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", "true");
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "");

        var cs = _postgres.ConnectionString;

        _factory = new WebApplicationFactory<OrderApp::Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:OrderDb"] = cs,
                        ["OTEL_SDK_DISABLED"] = "true",
                    });
                });
                builder.UseSetting("ConnectionStrings:OrderDb", cs);

                builder.ConfigureTestServices(services =>
                {
                    var toRemove = services
                        .Where(d => d.ServiceType == typeof(CatalogService.CatalogServiceClient))
                        .ToList();
                    foreach (var d in toRemove)
                        services.Remove(d);

                    // Return Success=false → ReserveInventoryStep throws SagaStepFailedException.
                    services.AddSingleton<CatalogService.CatalogServiceClient>(
                        new FakeCatalogServiceClient(reserveSuccess: false, failureReason: "out of stock"));

                    ReRegisterAsTransient<OrderApp::Kun.Sample.Order.Application.Sagas.OrderCreationSaga>(services);
                    ReRegisterAsTransient<OrderApp::Kun.Sample.Order.Application.Sagas.Steps.ReserveInventoryStep>(services);
                    ReRegisterAsTransient<OrderApp::Kun.Sample.Order.Application.Sagas.Steps.ProcessPaymentStep>(services);
                    ReRegisterAsTransient<OrderApp::Kun.Sample.Order.Application.Sagas.Steps.ShipOrderStep>(services);
                    ReRegisterAsTransient<OrderApp::Kun.Sample.Order.Application.Sagas.Steps.NotifyCustomerStep>(services);

                    static void ReRegisterAsTransient<T>(IServiceCollection svc) where T : class
                    {
                        var existing = svc.Where(d => d.ServiceType == typeof(T)).ToList();
                        foreach (var d in existing) svc.Remove(d);
                        svc.AddTransient<T>();
                    }
                });
            });

        Client = _factory.CreateClient();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
        Environment.SetEnvironmentVariable("ConnectionStrings__OrderDb", null);
    }
}

/// <summary>
/// End-to-end saga tests for the Order sample service.
/// Uses a shared WebApplicationFactory with a fake gRPC CatalogServiceClient.
/// </summary>
public sealed class OrderSagaTests : IClassFixture<OrderWebFactory>
{
    private readonly HttpClient _client;

    /// <summary>Injects the shared factory fixture.</summary>
    public OrderSagaTests(OrderWebFactory factory)
    {
        _client = factory.Client;
    }

    /// <summary>
    /// POST /demo/happy-path returns 200 with an orderId; polling eventually sees State=Completed.
    /// </summary>
    [Fact]
    public async Task HappyPath_SagaEventuallyCompletes()
    {
        var ct = TestContext.Current.CancellationToken;

        var postResponse = await _client.PostAsJsonAsync("/demo/happy-path", new { }, ct);
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            because: $"server response: {await postResponse.Content.ReadAsStringAsync(ct)} | " +
                     $"captured error: {CapturingLoggerProvider.Instance.LastError}");

        var body = await postResponse.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        var sagaId = doc.RootElement.GetProperty("orderId").GetGuid();
        sagaId.Should().NotBeEmpty();

        var state = await PollSagaStateAsync(sagaId, expectedState: "Completed", timeoutSeconds: 30, ct);
        state.Should().Be("Completed",
            because: "all 4 saga steps should complete when no failure is simulated");
    }

    /// <summary>
    /// POST /demo/trigger-payment-failure → ProcessPaymentStep throws → saga compensates → State=Compensated.
    /// </summary>
    [Fact]
    public async Task TriggerPaymentFailure_SagaEventuallyCompensates()
    {
        var ct = TestContext.Current.CancellationToken;

        var postResponse = await _client.PostAsJsonAsync("/demo/trigger-payment-failure", new { }, ct);
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await postResponse.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        var sagaId = doc.RootElement.GetProperty("orderId").GetGuid();

        var state = await PollSagaStateAsync(sagaId, expectedState: "Compensated", timeoutSeconds: 30, ct);
        state.Should().Be("Compensated",
            because: "ProcessPaymentStep throws → orchestrator compensates ReserveInventoryStep");
    }

    /// <summary>
    /// POST /demo/trigger-timeout → ShipOrderStep sleeps 60s past its 10s timeout;
    /// SagaTimeoutDetector fires compensation → State=Compensated.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task TriggerTimeout_SagaEventuallyCompensates()
    {
        var ct = TestContext.Current.CancellationToken;

        var postResponse = await _client.PostAsJsonAsync("/demo/trigger-timeout", new { }, ct);
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await postResponse.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        var sagaId = doc.RootElement.GetProperty("orderId").GetGuid();

        var state = await PollSagaStateAsync(sagaId, expectedState: "Compensated", timeoutSeconds: 90, ct);
        state.Should().Be("Compensated",
            because: "ShipOrderStep sleeps past its 10s timeout; SagaTimeoutDetector compensates");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<string?> PollSagaStateAsync(
        Guid sagaId,
        string expectedState,
        int timeoutSeconds,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        string? lastState = null;
        string? lastFailureReason = null;

        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var response = await _client.GetAsync($"/demo/sagas/{sagaId}", ct);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                var doc = JsonDocument.Parse(json);
                lastState = doc.RootElement.GetProperty("state").GetString();
                if (doc.RootElement.TryGetProperty("failureReason", out var fr))
                    lastFailureReason = fr.GetString();
                if (lastState == expectedState)
                    return lastState;
                // Stop early on terminal non-expected states (Failed).
                if (lastState == "Failed")
                    throw new InvalidOperationException(
                        $"Saga {sagaId} reached terminal state 'Failed' (expected '{expectedState}'). " +
                        $"FailureReason: {lastFailureReason}");
            }

            await Task.Delay(500, ct);
        }

        return lastState;
    }
}

/// <summary>
/// Tests that exercise the ReserveInventoryStep specifically:
/// when Catalog gRPC returns Success=false, ExecuteAsync throws SagaStepFailedException
/// and the saga reaches Compensated state (no subsequent steps ran so nothing to undo
/// beyond the order status update in CompensateAsync).
/// </summary>
public sealed class ReserveInventoryStepFailureTests : IClassFixture<OrderWebFactoryWithInventoryFailure>
{
    private readonly HttpClient _client;

    /// <summary>Injects the shared factory fixture.</summary>
    public ReserveInventoryStepFailureTests(OrderWebFactoryWithInventoryFailure factory)
    {
        _client = factory.Client;
    }

    /// <summary>
    /// When ReserveStockAsync returns Success=false, ReserveInventoryStep throws
    /// SagaStepFailedException, the saga orchestrator compensates immediately
    /// (no subsequent steps have run), and the saga state reaches Compensated.
    /// </summary>
    [Fact]
    public async Task InventoryReservationFailure_SagaCompensates()
    {
        var ct = TestContext.Current.CancellationToken;

        // Use the happy-path endpoint — the fake client is wired to return Success=false,
        // so ReserveInventoryStep will fail regardless of which endpoint is called.
        var postResponse = await _client.PostAsJsonAsync("/demo/happy-path", new { }, ct);
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await postResponse.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        var sagaId = doc.RootElement.GetProperty("orderId").GetGuid();
        sagaId.Should().NotBeEmpty();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        string? lastState = null;

        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var response = await _client.GetAsync($"/demo/sagas/{sagaId}", ct);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                var stateDoc = JsonDocument.Parse(json);
                lastState = stateDoc.RootElement.GetProperty("state").GetString();
                if (lastState is "Compensated" or "Failed")
                    break;
            }

            await Task.Delay(100, ct);
        }

        lastState.Should().Be("Compensated",
            because: "ReserveInventoryStep throws SagaStepFailedException when Catalog returns Success=false, " +
                     "triggering immediate saga compensation");
    }
}

/// <summary>
/// Diagnostic logger provider — keeps the most recent error-level entry so the test
/// assertion can show the server-side exception that caused a 500, rather than just
/// the ProblemDetails envelope.
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    internal static readonly CapturingLoggerProvider Instance = new();

    internal string? LastError { get; private set; }

    public ILogger CreateLogger(string categoryName) => new Capturing(this, categoryName);

    public void Dispose() { }

    private sealed class Capturing : ILogger
    {
        private readonly CapturingLoggerProvider _owner;
        private readonly string _category;

        public Capturing(CapturingLoggerProvider owner, string category)
        {
            _owner = owner;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Error) return;
            _owner.LastError = $"[{_category}] {formatter(state, exception)}\n{exception}";
        }
    }
}

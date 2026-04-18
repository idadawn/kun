using System.Diagnostics;

using Kun.Core.Cqrs;
using Kun.Core.Time;
using Kun.Mediator;
using Kun.Mediator.Behaviors;
using Kun.Mediator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

// ----- Composition root -----

var services = new ServiceCollection();

// Minimal logging: NullLogger is in Microsoft.Extensions.Logging.Abstractions, so no
// Microsoft.Extensions.Logging concrete dependency is needed for the sample.
services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

services.AddSingleton<ISystemClock>(SystemClock.Instance);

services.AddKunMediator(builder =>
{
    builder.AddRequestHandler<GreetCommand, string, GreetHandler>();
    builder.AddRequestHandler<NowQuery, DateTimeOffset, NowHandler>();

    builder.AddPipelineBehavior<GreetCommand, string, LoggingBehavior<GreetCommand, string>>();
    builder.AddPipelineBehavior<GreetCommand, string, PerformanceBehavior<GreetCommand, string>>();
    builder.AddPipelineBehavior<GreetCommand, string, TracingBehavior<GreetCommand, string>>();

    builder.AddNotificationHandler<UserJoined, EmailSender>();
    builder.AddNotificationHandler<UserJoined, AnalyticsTracker>();
});

using var provider = services.BuildServiceProvider();
var sender = provider.GetRequiredService<ISender>();
var publisher = provider.GetRequiredService<IPublisher>();

// Attach a console ActivityListener so TracingBehavior output is visible.
using var listener = new ActivityListener
{
    ShouldListenTo = s => s.Name == "Kun.Mediator",
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    ActivityStopped = a =>
        Console.WriteLine($"  [Activity] {a.DisplayName} — {a.Duration.TotalMilliseconds:F2} ms"),
};
ActivitySource.AddActivityListener(listener);

Console.WriteLine("Kun Framework sample — Mediator dispatch demo");
Console.WriteLine();

Console.WriteLine("1) Command: GreetCommand");
var greeting = await sender.Send(new GreetCommand("world"));
Console.WriteLine($"   → {greeting}");
Console.WriteLine();

Console.WriteLine("2) Query: NowQuery");
var now = await sender.Send(new NowQuery());
Console.WriteLine($"   → {now:O}");
Console.WriteLine();

Console.WriteLine("3) Notification: UserJoined (two handlers)");
await publisher.Publish(new UserJoined("dawn"));
Console.WriteLine();

Console.WriteLine("Done.");

// ----- Sample domain types -----

file sealed record GreetCommand(string Name) : IRequest<string>;

file sealed class GreetHandler(ISystemClock clock) : IRequestHandler<GreetCommand, string>
{
    public ValueTask<string> Handle(GreetCommand request, CancellationToken cancellationToken)
        => ValueTask.FromResult($"[{clock.UtcNow:O}] hello, {request.Name}!");
}

file sealed record NowQuery : IRequest<DateTimeOffset>;

file sealed class NowHandler(ISystemClock clock) : IRequestHandler<NowQuery, DateTimeOffset>
{
    public ValueTask<DateTimeOffset> Handle(NowQuery request, CancellationToken cancellationToken)
        => ValueTask.FromResult(clock.UtcNow);
}

file sealed record UserJoined(string UserName) : INotification;

file sealed class EmailSender : INotificationHandler<UserJoined>
{
    public ValueTask Handle(UserJoined notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"  [EmailSender] Welcome email queued for {notification.UserName}");
        return ValueTask.CompletedTask;
    }
}

file sealed class AnalyticsTracker : INotificationHandler<UserJoined>
{
    public ValueTask Handle(UserJoined notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"  [AnalyticsTracker] user_joined:{notification.UserName}");
        return ValueTask.CompletedTask;
    }
}

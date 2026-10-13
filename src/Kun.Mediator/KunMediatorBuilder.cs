using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;
using Kun.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kun.Mediator;

/// <summary>
/// Fluent builder used to register request handlers, pipeline behaviors, and notification
/// handlers with <see cref="KunMediator"/>. All registrations are performed via
/// compile-time-closed generics — no runtime reflection or <c>MakeGenericType</c> occurs.
/// </summary>
public sealed class KunMediatorBuilder
{
    // (requestType → wrapper factory). The factory closes over the behavior list by reference
    // so behaviors registered after AddRequestHandler but before Build() are still included.
    private readonly Dictionary<string, CapabilityDescriptor> _capabilities = new(StringComparer.Ordinal);
    private readonly List<(Type RequestType, Func<IServiceProvider, RequestHandlerBase> Factory)> _handlerEntries = [];

    // Per TRequest: ordered list of behavior factories (typed as object).
    // AddPipelineBehavior<TReq,TResp,TBehavior> guarantees each is IPipelineBehavior<TReq,TResp>.
    private readonly Dictionary<Type, List<Func<IServiceProvider, object>>> _behaviorEntries = [];

    // 已经由 AddPermissionBehavior 装配权限判定的请求类型；Build() 据此拒绝漏装的受保护请求。
    private readonly HashSet<Type> _permissionGuarded = [];

    // Per TNotification: the shared, growable factory list stored as object (non-generic dict).
    // Each entry is actually List<Func<IServiceProvider, INotificationHandler<TNotification>>>.
    // A separate dispatcher builder (also closed over the list) is stored in _notificationBuilders.
    private readonly Dictionary<Type, object> _notificationFactoryLists = [];

    // Per TNotification: a builder delegate → NotificationDispatcher<TNotification> (as object).
    // Registered once per notification type; the builder closes over the live factory list so
    // all subsequently added handlers are automatically included at Build() time.
    private readonly Dictionary<Type, Func<IServiceProvider, object>> _notificationBuilders = [];

    /// <summary>Initialises a new builder bound to the given <paramref name="services"/>.</summary>
    /// <param name="services">The service collection to register DI entries into.</param>
    public KunMediatorBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
        services.TryAddSingleton<ICapabilityRegistry>(_ => new CapabilityRegistry(_capabilities.Values));
    }

    /// <summary>The service collection that handler and behavior types are registered into.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Registers a request handler. Also registers <typeparamref name="THandler"/> as a
    /// transient service so the DI container can resolve it.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <typeparam name="THandler">The handler implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public KunMediatorBuilder AddRequestHandler<TRequest, TResponse, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>()
        where TRequest : Kun.Core.Cqrs.IRequest<TResponse>
        where THandler : class, Kun.Core.Cqrs.IRequestHandler<TRequest, TResponse>
    {
        Services.AddTransient<THandler>();

        if (!_behaviorEntries.ContainsKey(typeof(TRequest)))
        {
            _behaviorEntries[typeof(TRequest)] = [];
        }

        // Capture list reference so behaviors registered after this call are still included.
        var behaviorList = _behaviorEntries[typeof(TRequest)];

        _handlerEntries.Add((typeof(TRequest), _ =>
        {
            // Cast each object-typed factory to the strongly-typed delegate.
            // Safe: AddPipelineBehavior<TRequest,TResponse,TBehavior> guarantees the type.
            var typedFactories = behaviorList
                .Select(f =>
                    (Func<IServiceProvider, IPipelineBehavior<TRequest, TResponse>>)(sp =>
                        (IPipelineBehavior<TRequest, TResponse>)f(sp)))
                .ToList();

            return new RequestHandlerWrapper<TRequest, TResponse>(
                sp => sp.GetRequiredService<THandler>(),
                typedFactories);
        }
        ));

        return this;
    }

    /// <summary>注册 handler 的同时登记稳定能力 ID 与源生成请求／响应 Schema。</summary>
    public KunMediatorBuilder AddRequestHandler<TRequest, TResponse, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
        string capabilityId, JsonTypeInfo<TRequest> request, JsonTypeInfo<TResponse> response)
        where TRequest : Kun.Core.Cqrs.IRequest<TResponse>
        where THandler : class, Kun.Core.Cqrs.IRequestHandler<TRequest, TResponse>
    {
        var descriptor = new CapabilityDescriptor(capabilityId, request, response);
        if (!_capabilities.TryAdd(capabilityId, descriptor)) throw new InvalidOperationException($"能力 ID 重复：{capabilityId}");
        return AddRequestHandler<TRequest, TResponse, THandler>();
    }

    /// <summary>
    /// Registers a pipeline behavior for the given request/response pair. Behaviors are
    /// applied in registration order (outermost first). Also registers
    /// <typeparamref name="TBehavior"/> as a transient service.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <typeparam name="TBehavior">The behavior implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public KunMediatorBuilder AddPipelineBehavior<TRequest, TResponse, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TBehavior>()
        where TRequest : Kun.Core.Cqrs.IRequest<TResponse>
        where TBehavior : class, IPipelineBehavior<TRequest, TResponse>
    {
        Services.AddTransient<TBehavior>();

        if (!_behaviorEntries.ContainsKey(typeof(TRequest)))
        {
            _behaviorEntries[typeof(TRequest)] = [];
        }

        _behaviorEntries[typeof(TRequest)].Add(sp => sp.GetRequiredService<TBehavior>());
        return this;
    }

    /// <summary>登记 <paramref name="requestType"/> 已装配权限行为，供 Build() 的运行期兜底核对。</summary>
    internal void MarkPermissionGuarded(Type requestType) => _permissionGuarded.Add(requestType);

    /// <summary>
    /// Registers a notification handler. Multiple handlers may be registered for the same
    /// notification type; they are invoked sequentially in registration order.
    /// Also registers <typeparamref name="THandler"/> as a transient service.
    /// </summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <typeparam name="THandler">The handler implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public KunMediatorBuilder AddNotificationHandler<TNotification, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>()
        where TNotification : INotification
        where THandler : class, INotificationHandler<TNotification>
    {
        Services.AddTransient<THandler>();

        // First handler for this notification type: create the factory list and register a builder
        // that closes over it. TNotification is captured at compile time — zero MakeGenericType.
        if (!_notificationFactoryLists.ContainsKey(typeof(TNotification)))
        {
            var factories = new List<Func<IServiceProvider, INotificationHandler<TNotification>>>();
            _notificationFactoryLists[typeof(TNotification)] = factories;

            // Builder closes over the live list; all subsequent Add calls extend the same list
            // so this single builder always reflects the complete factory set at Build() time.
            _notificationBuilders[typeof(TNotification)] =
                sp => new NotificationDispatcher<TNotification>(factories);
        }

        // Append the new handler factory to the live list captured by the builder above.
        var list = (List<Func<IServiceProvider, INotificationHandler<TNotification>>>)
            _notificationFactoryLists[typeof(TNotification)];
        list.Add(sp => sp.GetRequiredService<THandler>());

        return this;
    }

    /// <summary>
    /// Builds the <see cref="KunMediator"/> instance, freezing all registrations into
    /// <see cref="FrozenDictionary{TKey,TValue}"/> for O(1) dispatch. Called once at
    /// application startup by the DI factory.
    /// </summary>
    /// <param name="scopeFactory">The <see cref="IServiceScopeFactory"/> used to create a child scope per dispatch.</param>
    /// <returns>A fully configured, immutable <see cref="KunMediator"/>.</returns>
    internal KunMediator Build(IServiceScopeFactory scopeFactory)
    {
        // 运行期兜底：实现 IPermissionProtectedRequest 的请求必须同时装配权限行为。
        // 直接或经反射调用 AddRequestHandler 绕过编译期门禁时，Mediator 不构建，处理器不会被执行。
        var unguarded = _handlerEntries
            .Select(e => e.RequestType)
            .Where(type => typeof(IPermissionProtectedRequest).IsAssignableFrom(type) && !_permissionGuarded.Contains(type))
            .Select(type => type.FullName ?? type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unguarded.Length != 0)
        {
            throw new InvalidOperationException(
                "受保护请求未装配权限行为：" + string.Join(", ", unguarded) +
                "。请与处理器一起调用 AddPermissionBehavior<TRequest, TResponse>(denied)。");
        }

        // Handler/dispatcher wrapper instances are stateless — build them once with a null SP.
        // The actual SP used at dispatch time comes from the per-call scope created in KunMediator.
        var handlers = _handlerEntries
            .ToDictionary(e => e.RequestType, e => e.Factory(null!))
            .ToFrozenDictionary();

        var notificationDispatchers = _notificationBuilders
            .ToDictionary(kv => kv.Key, kv => kv.Value(null!))
            .ToFrozenDictionary();

        return new KunMediator(scopeFactory, handlers, notificationDispatchers);
    }
}

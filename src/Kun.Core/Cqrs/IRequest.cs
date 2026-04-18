using Kun.Core.Results;

namespace Kun.Core.Cqrs;

/// <summary>请求标记，包含返回类型信息。由 Kun.Mediator 调度。</summary>
public interface IRequest<TResponse> { }

/// <summary>Command：写操作请求，带返回值。</summary>
public interface ICommand<TResponse> : IRequest<TResponse> { }

/// <summary>Command：写操作请求，无返回值（仍通过 Result 标识成败）。</summary>
public interface ICommand : IRequest<Result> { }

/// <summary>Query：只读请求。应当在读侧走 Dapper AOT 等高性能路径。</summary>
public interface IQuery<TResponse> : IRequest<TResponse> { }

/// <summary>Handler 通用基类：处理 <typeparamref name="TRequest"/> 并返回 <typeparamref name="TResponse"/>。</summary>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    ValueTask<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

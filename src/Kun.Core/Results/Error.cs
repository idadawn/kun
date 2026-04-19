namespace Kun.Core.Results;

/// <summary>错误分类，用于映射到 HTTP/gRPC 状态码。</summary>
public enum ErrorType
{
    /// <summary>Generic unclassified failure.</summary>
    Failure = 0,
    /// <summary>Input validation failure.</summary>
    Validation,
    /// <summary>Requested resource was not found.</summary>
    NotFound,
    /// <summary>State conflict (e.g. duplicate, optimistic concurrency).</summary>
    Conflict,
    /// <summary>Caller is not authenticated.</summary>
    Unauthorized,
    /// <summary>Caller is authenticated but not permitted.</summary>
    Forbidden,
    /// <summary>A dependency is temporarily unavailable.</summary>
    Unavailable
}

/// <summary>
/// 强类型错误。相比抛异常，<see cref="Error"/> 是显式的、可测试的失败信号，
/// 也更符合 CQRS Handler 的函数式风格。
/// </summary>
/// <param name="Code">机器可读错误代码（如 <c>order.notFound</c>）。</param>
/// <param name="Message">人类可读错误描述。</param>
/// <param name="Type">错误分类，决定映射到哪个 HTTP/gRPC 状态码。</param>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    /// <summary>Sentinel value representing the absence of an error on a success result.</summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    /// <summary>Creates a generic <see cref="ErrorType.Failure"/> error.</summary>
    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);
    /// <summary>Creates an <see cref="ErrorType.Validation"/> error.</summary>
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    /// <summary>Creates a <see cref="ErrorType.NotFound"/> error.</summary>
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    /// <summary>Creates a <see cref="ErrorType.Conflict"/> error.</summary>
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    /// <summary>Creates an <see cref="ErrorType.Unauthorized"/> error.</summary>
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);
    /// <summary>Creates a <see cref="ErrorType.Forbidden"/> error.</summary>
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
    /// <summary>Creates an <see cref="ErrorType.Unavailable"/> error.</summary>
    public static Error Unavailable(string code, string message) => new(code, message, ErrorType.Unavailable);
}

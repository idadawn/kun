namespace Kun.Core.Results;

/// <summary>错误分类，用于映射到 HTTP/gRPC 状态码。</summary>
public enum ErrorType
{
    Failure = 0,
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
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
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    public static Error Failure(string code, string message)     => new(code, message, ErrorType.Failure);
    public static Error Validation(string code, string message)  => new(code, message, ErrorType.Validation);
    public static Error NotFound(string code, string message)    => new(code, message, ErrorType.NotFound);
    public static Error Conflict(string code, string message)    => new(code, message, ErrorType.Conflict);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);
    public static Error Forbidden(string code, string message)   => new(code, message, ErrorType.Forbidden);
    public static Error Unavailable(string code, string message) => new(code, message, ErrorType.Unavailable);
}

namespace Kun.Core.Results;

/// <summary>
/// 无返回值的 Result。Handler 在不需要返回数据时使用。
/// </summary>
public readonly record struct Result
{
    /// <summary><see langword="true"/> when the operation succeeded.</summary>
    public bool IsSuccess { get; }
    /// <summary><see langword="true"/> when the operation failed.</summary>
    public bool IsFailure => !IsSuccess;
    /// <summary>The error carried by a failure result; <see cref="Error.None"/> on success.</summary>
    public Error Error { get; }

    private Result(bool success, Error error)
    {
        if (success && error != Error.None)
            throw new InvalidOperationException("Success result cannot carry an error.");
        if (!success && error == Error.None)
            throw new InvalidOperationException("Failure result must carry an error.");

        IsSuccess = success;
        Error = error;
    }

    /// <summary>Creates a successful result.</summary>
    public static Result Ok() => new(true, Error.None);
    /// <summary>Creates a failure result carrying <paramref name="error"/>.</summary>
    public static Result Fail(Error error) => new(false, error);

    /// <summary>Implicit conversion from <see cref="Error"/> to a failure result.</summary>
    public static implicit operator Result(Error error) => Fail(error);
}

/// <summary>
/// 带返回值的 Result。Handler 成功时携带 <typeparamref name="T"/>，失败时携带 <see cref="Error"/>。
/// </summary>
public readonly record struct Result<T>
{
    private readonly T? _value;

    /// <summary><see langword="true"/> when the operation succeeded.</summary>
    public bool IsSuccess { get; }
    /// <summary><see langword="true"/> when the operation failed.</summary>
    public bool IsFailure => !IsSuccess;
    /// <summary>The error carried by a failure result; <see cref="Error.None"/> on success.</summary>
    public Error Error { get; }

    /// <summary>
    /// 仅在 <see cref="IsSuccess"/> 为 <see langword="true"/> 时可访问。
    /// 失败 Result 调用此属性会抛异常——应先用 <see cref="IsSuccess"/> 判断。
    /// </summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value of a failure result.");

    private Result(bool success, T? value, Error error)
    {
        IsSuccess = success;
        _value = value;
        Error = error;
    }

    /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
    public static Result<T> Ok(T value) => new(true, value, Error.None);
    /// <summary>Creates a failure result carrying <paramref name="error"/>.</summary>
    public static Result<T> Fail(Error error) => new(false, default, error);

    /// <summary>Implicit conversion from <typeparamref name="T"/> to a success result.</summary>
    public static implicit operator Result<T>(T value) => Ok(value);
    /// <summary>Implicit conversion from <see cref="Error"/> to a failure result.</summary>
    public static implicit operator Result<T>(Error error) => Fail(error);

    /// <summary>
    /// 在 Handler 直接返回给 gRPC 端点时使用，失败时抛出可被拦截器捕获的异常。
    /// </summary>
    public T EnsureSuccess() => IsSuccess
        ? _value!
        : throw new ResultFailureException(Error);
}

/// <summary>由 gRPC 拦截器识别并翻译为 <c>RpcException</c>。</summary>
public sealed class ResultFailureException : Exception
{
    /// <summary>The error that caused the failure.</summary>
    public Error Error { get; }

    /// <summary>Initializes a new instance from <paramref name="error"/>.</summary>
    public ResultFailureException(Error error) : base(error.Message)
    {
        Error = error;
    }
}

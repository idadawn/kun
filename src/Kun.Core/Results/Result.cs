namespace Kun.Core.Results;

/// <summary>
/// 无返回值的 Result。Handler 在不需要返回数据时使用。
/// </summary>
public readonly record struct Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
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

    public static Result Ok() => new(true, Error.None);
    public static Result Fail(Error error) => new(false, error);

    public static implicit operator Result(Error error) => Fail(error);
}

/// <summary>
/// 带返回值的 Result。Handler 成功时携带 <typeparamref name="T"/>，失败时携带 <see cref="Error"/>。
/// </summary>
public readonly record struct Result<T>
{
    private readonly T? _value;

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
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

    public static Result<T> Ok(T value) => new(true, value, Error.None);
    public static Result<T> Fail(Error error) => new(false, default, error);

    public static implicit operator Result<T>(T value) => Ok(value);
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
    public Error Error { get; }

    public ResultFailureException(Error error) : base(error.Message)
    {
        Error = error;
    }
}

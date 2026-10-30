namespace Pzd.SharedKernel;

/// <summary>
/// 业务失败用结果对象返回，不抛异常（架构书 §3.3、§8）。
/// 端点侧由 09 号票把 <see cref="Error"/> 映射成 ProblemDetails。
/// </summary>
public class Result
{
    protected Result(Error? error) => Error = error;

    /// <summary>成功时为 <c>null</c>；失败时就是那条领域错误。</summary>
    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public bool IsFailure => !IsSuccess;

    public static Result Success() => new(null);

    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(error);
    }
}

/// <summary>带值的结果：失败分支取值会抛异常，调用方应先看 <see cref="Result.IsSuccess"/>。</summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, Error? error)
        : base(error) => _value = value;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"失败的结果没有值，错误码：{Error!.Code}");

    public static Result<T> Success(T value) => new(value, null);

    public static new Result<T> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(default, error);
    }
}

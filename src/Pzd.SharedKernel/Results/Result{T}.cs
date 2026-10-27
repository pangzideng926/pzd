namespace Pzd.SharedKernel.Results;

/// <summary>
/// 带值的结果：成功时 <see cref="Value"/> 可用，失败时访问 <see cref="Value"/> 抛异常，
/// 避免“失败也带默认值”的静默错误。
/// </summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T value)
        : base(true, null) => _value = value;

    internal Result(Error error)
        : base(false, error)
    {
    }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("The value of a failed result cannot be accessed.");

    public static implicit operator Result<T>(T value) => new(value);
}

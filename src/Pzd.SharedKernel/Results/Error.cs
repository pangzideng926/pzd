using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace Pzd.SharedKernel.Results;

/// <summary>
/// 领域错误：按 <see cref="Code"/> 相等，携带类型、文案与可选元数据。
/// 错误码使用 PascalCase 的点分段：模块.实体.原因（如 <c>Identity.User.NameDuplicated</c>）；
/// 平台级错误允许两段（如 <c>Tenant.NotFound</c>，见架构 §6.2）。
/// </summary>
public sealed class Error : IEquatable<Error>
{
    private static readonly Regex CodeFormat = new(
        @"^[A-Z][A-Za-z0-9]*(\.[A-Z][A-Za-z0-9]*){1,2}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, object?> EmptyMetadata =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(StringComparer.Ordinal));

    public Error(string code, ErrorType type, string message, IReadOnlyDictionary<string, object?>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (!IsValidCode(code))
        {
            throw new ArgumentException(
                $"Invalid error code '{code}': expected 'Module.Entity.Reason' (or 'Module.Reason' for platform errors) in PascalCase.",
                nameof(code));
        }

        Code = code;
        Type = type;
        Message = message;
        Metadata = CreateSnapshot(metadata);
    }

    public string Code { get; }

    public ErrorType Type { get; }

    public string Message { get; }

    public IReadOnlyDictionary<string, object?> Metadata { get; }

    public static bool IsValidCode(string? code) =>
        !string.IsNullOrWhiteSpace(code) && CodeFormat.IsMatch(code);

    public bool Equals(Error? other) =>
        other is not null && string.Equals(Code, other.Code, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is Error other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Code);

    public override string ToString() => $"{Code}: {Message}";

    /// <summary>
    /// 构造时复制成 Ordinal 键的只读快照：调用方之后改动原字典不会影响已创建的 Error，
    /// 调用方也无法通过强转写回；空元数据复用共享的 <see cref="EmptyMetadata"/>，不重复分配。
    /// </summary>
    private static IReadOnlyDictionary<string, object?> CreateSnapshot(
        IReadOnlyDictionary<string, object?>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return EmptyMetadata;
        }

        var snapshot = new Dictionary<string, object?>(metadata.Count, StringComparer.Ordinal);

        foreach (var pair in metadata)
        {
            snapshot[pair.Key] = pair.Value;
        }

        return new ReadOnlyDictionary<string, object?>(snapshot);
    }
}

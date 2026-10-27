namespace Pzd.SharedKernel.Abstractions;

/// <summary>
/// 当前请求用户的最小信息；未登录时 <see cref="IsAuthenticated"/> 为 false，其余为空。
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    long? UserId { get; }

    string? UserName { get; }
}

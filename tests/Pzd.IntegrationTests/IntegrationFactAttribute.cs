using System.Runtime.CompilerServices;

namespace Pzd.IntegrationTests;

/// <summary>
/// 需要本机真实 MySQL 的用例：发现期做一次动态探测，不可用时整条用例 Skip（状态 Skipped，退出码 0），
/// 原因只允许是 <see cref="MySqlAvailability.CredentialsSkipReason"/> / <see cref="MySqlAvailability.UnreachableSkipReason"/> /
/// <see cref="MySqlAvailability.PrivilegeSkipReason"/> 三种之一（票 10）。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (MySqlAvailability.Probe().SkipReason is { } reason)
        {
            Skip = reason;
        }
    }
}

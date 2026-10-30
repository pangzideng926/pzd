namespace Pzd.SharedKernel.UnitTests;

/// <summary>
/// 05 号票 seam S4：只自证包引用边界——共享内核不引用 SqlSugar / ASP.NET Core。
/// 真正的分层守卫（Domain 不引 Infrastructure、不引 ORM 特性）在 13 号票，S4 不替代它。
/// </summary>
public sealed class DependencyBoundaryTests
{
    [Fact]
    public void 共享内核不引用SqlSugar与AspNetCore()
    {
        var referenced = typeof(Entity).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(referenced, name => name.StartsWith("SqlSugar", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referenced, name => name.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase));
    }
}

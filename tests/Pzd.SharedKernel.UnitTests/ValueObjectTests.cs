using System.Reflection;

namespace Pzd.SharedKernel.UnitTests;

/// <summary>05 号票 seam S1：值对象按值相等且不可变。</summary>
public sealed class ValueObjectTests
{
    [Fact]
    public void 值相同的两个值对象相等且哈希一致()
    {
        var first = new Money(100m, "CNY");
        var second = new Money(100m, "CNY");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void 任一组成分量不同则不相等()
    {
        var first = new Money(100m, "CNY");

        Assert.NotEqual(first, new Money(100.01m, "CNY"));
        Assert.NotEqual(first, new Money(100m, "USD"));
    }

    [Fact]
    public void 与不同类型的对象比较不相等()
    {
        var money = new Money(100m, "CNY");

        Assert.False(money.Equals("100 CNY"));
        Assert.False(money.Equals(null));
    }

    [Fact]
    public void 分量相同但类型不同的值对象不相等()
    {
        var money = new Money(100m, "CNY");
        ValueObject twin = new MoneyTwin(100m, "CNY");

        Assert.False(money.Equals(twin));
        Assert.False(twin.Equals(money));
        Assert.NotEqual<ValueObject>(money, twin);
    }

    [Fact]
    public void 值对象子类不暴露可写属性()
    {
        // 不可变靠约定：基类在语言上强制不了，示例与反射断言一起守（AC2）
        var writable = typeof(Money)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is { IsPublic: true })
            .Select(property => property.Name)
            .ToArray();

        Assert.Empty(writable);
    }

    [Fact]
    public void 相等运算符与相等方法一致()
    {
        var first = new Money(100m, "CNY");
        var second = new Money(100m, "CNY");

        Assert.True(first == second);
        Assert.False(first != second);
    }
}

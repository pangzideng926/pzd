using System.Reflection;

namespace Pzd.SharedKernel.UnitTests;

/// <summary>05 号票 seam S3：聚合根收集领域事件的顺序、去重与清空。</summary>
public sealed class AggregateRootTests
{
    [Fact]
    public void 事件按添加顺序收集()
    {
        var order = new Order("SO-001");
        var first = new OrderNumberChanged(order.Id, "SO-001");
        var second = new OrderNumberChanged(order.Id, "SO-002");

        order.Raise(first);
        order.Raise(second);

        Assert.Equal([first, second], order.DomainEvents);
    }

    [Fact]
    public void 同一个事件实例重复添加只收集一次()
    {
        var order = new Order("SO-001");
        var changed = new OrderNumberChanged(order.Id, "SO-001");

        order.Raise(changed);
        order.Raise(changed);

        Assert.Single(order.DomainEvents);
    }

    [Fact]
    public void 清空后不再残留事件()
    {
        var order = new Order("SO-001");
        order.Raise(new OrderNumberChanged(order.Id, "SO-001"));

        order.ClearDomainEvents();

        Assert.Empty(order.DomainEvents);
    }

    [Fact]
    public void 没有事件时收集结果是空的()
    {
        var order = new Order("SO-001");

        Assert.Empty(order.DomainEvents);
    }

    [Fact]
    public void 收集空事件抛参数异常()
    {
        var order = new Order("SO-001");

        Assert.Throws<ArgumentNullException>(() => order.Raise(null!));
    }

    [Fact]
    public void 改单号方法生效()
    {
        var order = new Order("SO-001");

        order.ChangeNumber("SO-002");

        Assert.Equal("SO-002", order.Number);
    }

    [Fact]
    public void 聚合属性不对外暴露setter()
    {
        // 「状态只通过方法变更」的另一半：属性本身不能有 public setter
        var writable = typeof(Order)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is { IsPublic: true })
            .Select(property => property.Name)
            .ToArray();

        Assert.Empty(writable);
    }

    [Fact]
    public void 领域事件集合不能被调用方强转后增删()
    {
        var order = new Order("SO-001");
        order.Raise(new OrderNumberChanged(order.Id, "SO-001"));

        Assert.False(order.DomainEvents is List<IDomainEvent>, "DomainEvents 直接吐了内部 List");
        Assert.Throws<InvalidCastException>(() => (List<IDomainEvent>)order.DomainEvents);

        // 即使按 IList 访问，写操作也必须被拒绝
        var asList = (IList<IDomainEvent>)order.DomainEvents;
        Assert.Throws<NotSupportedException>(() => asList.Add(new OrderNumberChanged(order.Id, "SO-999")));
    }
}

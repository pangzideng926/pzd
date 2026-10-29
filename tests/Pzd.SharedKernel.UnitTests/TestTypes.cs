namespace Pzd.SharedKernel.UnitTests;

/// <summary>测试用值对象：金额 + 币种，用来验证按值相等。</summary>
internal sealed class Money(decimal amount, string currency) : ValueObject
{
    public decimal Amount { get; } = amount;

    public string Currency { get; } = currency;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }
}

/// <summary>测试用值对象（与 <see cref="Money"/> 分量完全相同、类型不同），用来守「类型不同即不相等」。</summary>
internal sealed class MoneyTwin(decimal amount, string currency) : ValueObject
{
    public decimal Amount { get; } = amount;

    public string Currency { get; } = currency;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }
}

/// <summary>测试用聚合根：只暴露「改单号」一个状态变更方法，属性 private set。</summary>
internal sealed class Order : AggregateRoot
{
    public Order(string number) => Number = number;

    public string Number { get; private set; }

    public void ChangeNumber(string number) => Number = number;
}

/// <summary>测试用领域事件。</summary>
internal sealed record OrderNumberChanged(long OrderId, string Number) : IDomainEvent;

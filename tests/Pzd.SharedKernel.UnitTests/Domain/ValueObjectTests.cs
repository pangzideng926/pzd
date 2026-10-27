using Pzd.SharedKernel.Domain;

namespace Pzd.SharedKernel.UnitTests.Domain;

public class ValueObjectTests
{
    [Fact]
    public void Value_objects_with_equal_components_are_equal()
    {
        var first = new Money(10.5m, "CNY");
        var second = new Money(10.5m, "CNY");

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Value_objects_with_different_components_are_not_equal()
    {
        var first = new Money(10.5m, "CNY");
        var second = new Money(10.5m, "USD");

        Assert.NotEqual(first, second);
        Assert.True(first != second);
    }

    [Fact]
    public void Value_objects_of_different_types_are_not_equal()
    {
        var money = new Money(10.5m, "CNY");
        var otherShape = new Amount(10.5m, "CNY");

        Assert.False(money.Equals(otherShape));
    }

    private sealed class Money : ValueObject
    {
        public Money(decimal amount, string currency)
        {
            Amount = amount;
            Currency = currency;
        }

        public decimal Amount { get; }

        public string Currency { get; }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }
    }

    private sealed class Amount : ValueObject
    {
        public Amount(decimal value, string currency)
        {
            Value = value;
            Currency = currency;
        }

        public decimal Value { get; }

        public string Currency { get; }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Value;
            yield return Currency;
        }
    }
}

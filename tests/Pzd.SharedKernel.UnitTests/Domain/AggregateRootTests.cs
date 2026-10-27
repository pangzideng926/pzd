using Pzd.SharedKernel.Domain;

namespace Pzd.SharedKernel.UnitTests.Domain;

public class AggregateRootTests
{
    [Fact]
    public void Aggregate_collects_domain_events_in_order()
    {
        var order = new Order(1);

        order.Place("PZD-1");
        order.Place("PZD-2");

        Assert.Collection(
            order.DomainEvents,
            domainEvent => Assert.Equal("PZD-1", Assert.IsType<OrderPlaced>(domainEvent).Number),
            domainEvent => Assert.Equal("PZD-2", Assert.IsType<OrderPlaced>(domainEvent).Number));
    }

    [Fact]
    public void ClearEvents_empties_collected_events()
    {
        var order = new Order(1);
        order.Place("PZD-1");

        order.ClearEvents();

        Assert.Empty(order.DomainEvents);
    }

    [Fact]
    public void Collector_interface_exposes_and_clears_events()
    {
        IDomainEventCollector collector = new Order(7);
        ((Order)collector).Place("PZD-7");

        Assert.Single(collector.DomainEvents);

        collector.ClearEvents();

        Assert.Empty(collector.DomainEvents);
    }

    [Fact]
    public void Entities_with_same_type_and_id_are_equal()
    {
        var first = new Order(3);
        var second = new Order(3);
        var other = new Order(4);

        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Entities_of_different_types_with_the_same_id_are_not_equal()
    {
        var order = new Order(5);
        var invoice = new Invoice(5);
        var anotherInvoice = new Invoice(5);

        Assert.NotEqual<Entity<long>>(order, invoice);
        Assert.False(order.Equals((object)invoice));
        Assert.False(order == invoice);
        Assert.Equal(invoice, anotherInvoice);
    }

    [Fact]
    public void Adding_a_null_domain_event_throws()
    {
        var order = new Order(1);

        Assert.Throws<ArgumentNullException>(() => order.AddNullEvent());
    }

    private sealed class Order : AggregateRoot<long>
    {
        public Order(long id)
            : base(id)
        {
        }

        public void Place(string number) => AddDomainEvent(new OrderPlaced(number, Id));

        public void AddNullEvent() => AddDomainEvent(null!);
    }

    private sealed class Invoice : AggregateRoot<long>
    {
        public Invoice(long id)
            : base(id)
        {
        }
    }

    private sealed record OrderPlaced(string Number, long OrderId) : IDomainEvent
    {
        public DateTimeOffset OccurredOn { get; } = new(2026, 10, 27, 0, 0, 0, TimeSpan.Zero);
    }
}

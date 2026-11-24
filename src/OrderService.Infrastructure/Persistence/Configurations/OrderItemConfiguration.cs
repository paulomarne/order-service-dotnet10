using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderService.Domain.Orders;
using OrderService.Domain.ValueObjects;

namespace OrderService.Infrastructure.Persistence.Configurations;

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items", table =>
            table.HasCheckConstraint("ck_order_items_quantity_positive", "quantity > 0"));

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.OrderId).IsRequired();
        builder.Property(item => item.ProductId).IsRequired();
        builder.Property(item => item.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(item => item.UnitPriceAmount).HasPrecision(18, 4).IsRequired();
        builder.Property(item => item.Currency)
            .HasConversion(currency => currency.Code, code => Currency.From(code))
            .HasMaxLength(3)
            .IsRequired();
        builder.Property(item => item.Quantity)
            .HasConversion(quantity => quantity.Value, value => new Quantity(value))
            .IsRequired();
        builder.Ignore(item => item.UnitPrice);
        builder.Ignore(item => item.LineTotal);
    }
}

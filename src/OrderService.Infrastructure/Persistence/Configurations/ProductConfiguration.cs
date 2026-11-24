using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderService.Domain.Products;
using OrderService.Domain.ValueObjects;

namespace OrderService.Infrastructure.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products", table =>
            table.HasCheckConstraint("ck_products_available_non_negative", "available_quantity >= 0"));

        builder.HasKey(product => product.Id);
        builder.Property(product => product.Id).ValueGeneratedNever();
        builder.Property(product => product.Sku).HasMaxLength(40).IsRequired();
        builder.HasIndex(product => product.Sku).IsUnique();
        builder.Property(product => product.Name).HasMaxLength(200).IsRequired();
        builder.Property(product => product.UnitPriceAmount).HasPrecision(18, 4).IsRequired();
        builder.Property(product => product.Currency)
            .HasConversion(currency => currency.Code, code => Currency.From(code))
            .HasMaxLength(3)
            .IsRequired();
        builder.Ignore(product => product.UnitPrice);
        builder.Property(product => product.AvailableQuantity).IsRequired();

        builder.UsePostgresXmin();
    }
}

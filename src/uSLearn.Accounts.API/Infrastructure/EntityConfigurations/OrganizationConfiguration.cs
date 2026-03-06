using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using uSLearn.Accounts.Domain.OrganizationAggregate;

namespace uSLearn.Accounts.Infrastructure.EntityConfigurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    private const string TableName = "Organization";
    public void Configure(EntityTypeBuilder<Organization> entity)
    {
        entity.ToTable(TableName);
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedNever();
        entity.Property(e => e.TenantId)
            .IsRequired();
        entity.Property(e => e.OrganizationType)
             .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(50);

        entity.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        entity.Property(e => e.LegalName)
            .IsRequired()
            .HasMaxLength(200);

        entity.OwnsOne(e => e.Address, address =>
        {
            address.Property(a => a.Street)
                .HasMaxLength(200);
            address.Property(a => a.City)
                .HasMaxLength(100);
            address.Property(a => a.State)
                .HasMaxLength(100);
            address.Property(a => a.PostalCode)
                .HasMaxLength(20);
            address.Property(a => a.Country)
                .HasMaxLength(100);
            address.Property(a => a.CountryCode)
                .HasMaxLength(10);
        });
        entity.Property(e => e.TaxNumber)
            .IsRequired()
            .HasMaxLength(50);

        entity.Property(e => e.TaxNumberType)
            .HasConversion<string>()
            .IsRequired();

        entity.Property(e => e.Language)
            .HasMaxLength(50);

        entity.Property(e => e.LanguageCode)
            .HasMaxLength(10);

        entity.Property(e => e.CurrencyCode)
            .HasMaxLength(10);
    }
}
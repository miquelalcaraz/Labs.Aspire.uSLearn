using Microsoft.EntityFrameworkCore;
using uSLearn.Core.IntegrationEventLogEF;
using uSLearn.Identity.Models;

namespace uSLearn.Identity.Infrastructure;

public class IdentityContext : DbContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();

    public IdentityContext(DbContextOptions<IdentityContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");

        // Idempotency configuration
        modelBuilder.UseEventIdempotency();

        // Entity configurations
        ConfigureTenant(modelBuilder);
        ConfigureAdminUser(modelBuilder);
    }

    private static void ConfigureTenant(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(builder =>
        {
            builder.ToTable("Tenants");
            builder.HasKey(t => t.Id);

            builder.Property(t => t.Name)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(t => t.OrganizationId)
                .IsRequired();

            builder.HasIndex(t => t.OrganizationId)
                .IsUnique();

            builder.Property(t => t.CreatedAt)
                .IsRequired();
        });
    }

    private static void ConfigureAdminUser(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdminUser>(builder =>
        {
            builder.ToTable("AdminUsers");
            builder.HasKey(u => u.Id);

            builder.Property(u => u.Email)
                .IsRequired()
                .HasMaxLength(255);

            builder.Property(u => u.TemporaryPassword)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(u => u.TenantId)
                .IsRequired();

            builder.Property(u => u.CreatedAt)
                .IsRequired();

            // Index for quick tenant lookups
            builder.HasIndex(u => u.TenantId);

            // Unique email per tenant
            builder.HasIndex(u => new { u.TenantId, u.Email })
                .IsUnique();
        });
    }
}

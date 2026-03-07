using Microsoft.EntityFrameworkCore;

namespace uSLearn.Core.IntegrationEventLogEF;

public static class IntegrationLogExtensions
{
    public static void UseIntegrationEventLogs(this ModelBuilder builder)
    {
        builder.Entity<IntegrationEventLogEntry>(builder =>
        {
            builder.ToTable("IntegrationEventLog");

            builder.HasKey(e => e.EventId);
        });
    }

    public static void UseEventIdempotency(this ModelBuilder builder)
    {
        builder.Entity<ProcessedIntegrationEvent>(builder =>
        {
            builder.ToTable("ProcessedIntegrationEvents");
            // Composite primary key: same event can be processed by different handlers
            builder.HasKey(e => new { e.EventId, e.HandlerName });
            builder.Property(e => e.HandlerName)
                .HasMaxLength(255)
                .IsRequired();
            builder.Property(e => e.ProcessedAt)
                .IsRequired();
            builder.Property(e => e.EventType)
                .HasMaxLength(255);
            // Index for querying by event ID
            builder.HasIndex(e => e.EventId);
            // Index for querying by processed date (useful for cleanup jobs)
            builder.HasIndex(e => e.ProcessedAt);
        });
    }
}

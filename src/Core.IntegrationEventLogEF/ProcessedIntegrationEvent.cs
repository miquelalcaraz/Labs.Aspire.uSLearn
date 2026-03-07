using System;

namespace uSLearn.Core.IntegrationEventLogEF;

/// <summary>
/// Tracks processed integration events to ensure idempotency.
/// Prevents duplicate processing when the same event is delivered multiple times.
/// </summary>
public class ProcessedIntegrationEvent
{
    /// <summary>
    /// The unique identifier of the integration event (from IntegrationEvent.Id)
    /// </summary>
    public Guid EventId { get; set; }

    /// <summary>
    /// The full type name of the handler that processed this event
    /// (e.g., "OrganizationCreatedIntegrationEventHandler")
    /// </summary>
    public string HandlerName { get; set; } = null!;

    /// <summary>
    /// Timestamp when the event was successfully processed
    /// </summary>
    public DateTime ProcessedAt { get; set; }

    /// <summary>
    /// Optional: Store the event type for auditing purposes
    /// </summary>
    public string? EventType { get; set; }
}

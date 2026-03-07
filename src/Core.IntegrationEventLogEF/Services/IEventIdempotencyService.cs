using System;
using System.Threading.Tasks;
namespace uSLearn.Core.IntegrationEventLogEF.Services;

/// <summary>
/// Service for managing integration event idempotency.
/// Ensures that the same event is not processed multiple times by the same handler.
/// </summary>
public interface IEventIdempotencyService
{
    /// <summary>
    /// Checks if an event has already been processed by a specific handler.
    /// </summary>
    /// <param name="eventId">The unique identifier of the integration event</param>
    /// <param name="handlerName">The name of the handler (typically handler type name)</param>
    /// <returns>True if the event was already processed, false otherwise</returns>
    Task<bool> IsProcessedAsync(Guid eventId, string handlerName);

    /// <summary>
    /// Marks an event as processed by a specific handler.
    /// </summary>
    /// <param name="eventId">The unique identifier of the integration event</param>
    /// <param name="handlerName">The name of the handler (typically handler type name)</param>
    /// <param name="eventType">Optional: The type name of the event for auditing</param>
    Task MarkAsProcessedAsync(Guid eventId, string handlerName, string? eventType = null);
}

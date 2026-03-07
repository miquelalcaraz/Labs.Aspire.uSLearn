using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;



namespace uSLearn.Core.IntegrationEventLogEF.Services;

/// <summary>
/// Database-backed implementation of event idempotency service.
/// Uses ProcessedIntegrationEvents table to track which events have been processed by which handlers.
/// </summary>
public class EventIdempotencyService<TContext> : IEventIdempotencyService where TContext : DbContext
{
    private readonly TContext _context;
    private readonly ILogger<IEventIdempotencyService> _logger;

    public EventIdempotencyService(
        TContext context,
        ILogger<IEventIdempotencyService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    }

    public async Task<bool> IsProcessedAsync(Guid eventId, string handlerName)
    {
        var exists = await _context.Set<ProcessedIntegrationEvent>()
            .AnyAsync(e => e.EventId == eventId && e.HandlerName == handlerName);

        if (exists)
        {
            _logger.LogInformation(
                "Event {EventId} already processed by {HandlerName}. Skipping duplicate.",
                eventId,
                handlerName);
        }

        return exists;
    }

    public async Task MarkAsProcessedAsync(Guid eventId, string handlerName, string? eventType = null)
    {
        var processedEvent = new ProcessedIntegrationEvent
        {
            EventId = eventId,
            HandlerName = handlerName,
            ProcessedAt = DateTime.UtcNow,
            EventType = eventType
        };

        _context.Set<ProcessedIntegrationEvent>().Add(processedEvent);

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Marked event {EventId} as processed by {HandlerName}",
            eventId,
            handlerName);
    }
}

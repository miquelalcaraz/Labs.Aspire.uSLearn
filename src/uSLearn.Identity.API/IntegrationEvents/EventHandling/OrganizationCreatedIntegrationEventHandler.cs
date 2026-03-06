using uSLearn.Core.EventBus.Abstractions;
using uSLearn.Identity.IntegrationEvents.Events;

namespace uSLearn.Identity.IntegrationEvents.EventHandling
{
    public class OrganizationCreatedIntegrationEventHandler : IIntegrationEventHandler<OrganizationCreatedIntegrationEvent>
    {
        private readonly ILogger<OrganizationCreatedIntegrationEventHandler> _logger;
        public OrganizationCreatedIntegrationEventHandler(ILogger<OrganizationCreatedIntegrationEventHandler> logger)
        {
            _logger = logger;
        }
        public async Task Handle(OrganizationCreatedIntegrationEvent @event)
        {
            _logger.LogInformation("Received integration event for organization created: {OrganizationId} - {Name}", @event.OrganizationId, @event.Name);
            await Task.CompletedTask;
        }
    }

}

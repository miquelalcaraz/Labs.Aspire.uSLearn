using MediatR;

using uSLearn.Accounts.API.Domain.OrganizationAggregate;
using uSLearn.EventBus.Abstractions;

namespace uSLearn.Accounts.API.Domain.Events
{
    public class OrganizationCreatedDomainEventHandler : INotificationHandler<OrganizationCreatedDomainEvent>
    {
        private readonly ILogger<OrganizationCreatedDomainEventHandler> _logger;
        private readonly IOrganizationRepository _organizationRepository;
        private readonly IEventBus _eventBus;
        public OrganizationCreatedDomainEventHandler(ILogger<OrganizationCreatedDomainEventHandler> logger, IOrganizationRepository organizationRepository, IEventBus eventBus)
        {
            _logger = logger;
            _organizationRepository = organizationRepository;
            _eventBus = eventBus;
        }
        public async Task Handle(OrganizationCreatedDomainEvent notification, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Organization created: {OrganizationId} - {Name}", notification.OrganizationId, notification.Name);
            Organization organization = await _organizationRepository.GetAsync(notification.OrganizationId);
            if (organization != null)
            {
                _logger.LogInformation("Publishing integration event for organization: {OrganizationId} - {Name}", organization.Id, organization.Name);
                await _eventBus.PublishAsync(
                    new OrganizationCreatedIntegrationEvent
                    (
                        organization.TenantId,
                        organization.Id,
                        organization.Name,
                        organization.LegalName,
                        organization.TaxNumber,
                        organization.Address?.CountryCode!
                    ));
            }

            await Task.CompletedTask;

        }
    }

}

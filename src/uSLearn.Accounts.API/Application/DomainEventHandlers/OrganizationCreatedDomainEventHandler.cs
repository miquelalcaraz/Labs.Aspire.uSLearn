using MediatR;

using uSLearn.Accounts.API.Application.IntegrationEvents;
using uSLearn.Accounts.API.Application.IntegrationEvents.Events;
using uSLearn.Accounts.API.Domain.Events;
using uSLearn.Accounts.API.Domain.OrganizationAggregate;
using uSLearn.EventBus.Abstractions;

namespace uSLearn.Accounts.API.Application.DomainEventHandlers
{
    public class OrganizationCreatedDomainEventHandler : INotificationHandler<OrganizationCreatedDomainEvent>
    {
        private readonly ILogger<OrganizationCreatedDomainEventHandler> _logger;
        private readonly IOrganizationRepository _organizationRepository;
        private readonly IAccountIntegrationEventService _accountIntegrationEventService;
        public OrganizationCreatedDomainEventHandler(ILogger<OrganizationCreatedDomainEventHandler> logger, IOrganizationRepository organizationRepository, IAccountIntegrationEventService accountIntegrationEventService)
        {
            _logger = logger;
            _organizationRepository = organizationRepository;
            _accountIntegrationEventService = accountIntegrationEventService;
        }
        public async Task Handle(OrganizationCreatedDomainEvent notification, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Organization created: {OrganizationId} - {Name}", notification.OrganizationId, notification.Name);
            Organization organization = await _organizationRepository.GetAsync(notification.OrganizationId);
            if (organization != null)
            {
                _logger.LogInformation("Publishing integration event for organization: {OrganizationId} - {Name}", organization.Id, organization.Name);
                var integrationEvent =
                     new OrganizationCreatedIntegrationEvent
                     (
                         organization.TenantId,
                         organization.Id,
                         organization.Name,
                         organization.LegalName,
                         organization.TaxNumber,
                         organization.Address?.CountryCode!
                     );
                await _accountIntegrationEventService.AddAndSaveEventAsync(integrationEvent);
            }

            await Task.CompletedTask;

        }
    }

}

using MediatR;

using uSLearn.EventBus.Events;

namespace uSLearn.Accounts.API.Application.IntegrationEvents.Events
{
    public record OrganizationCreatedIntegrationEvent(
        Guid TenantId,
        Guid OrganizationId,
        string Name,
        string LegalName,
        string TaxNumber,
        string CountryCode) :  IntegrationEvent;

}

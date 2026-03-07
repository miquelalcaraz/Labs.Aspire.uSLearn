using uSLearn.Core.EventBus.Events;

namespace uSLearn.Identity.IntegrationEvents.Events
{
    public record OrganizationCreatedIntegrationEvent(
        Guid TenantId,
        Guid OrganizationId,
        string Name,
        string LegalName,
        string TaxNumber,
        string CountryCode) : IntegrationEvent;

}

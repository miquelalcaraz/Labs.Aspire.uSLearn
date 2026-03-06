using uSLearn.EventBus.Events;

namespace uSLearn.Accounts.API.Application.IntegrationEvents;

public interface IAccountIntegrationEventService
{
    Task PublishEventsThroughEventBusAsync(Guid transactionId);
    Task AddAndSaveEventAsync(IntegrationEvent evt);
}

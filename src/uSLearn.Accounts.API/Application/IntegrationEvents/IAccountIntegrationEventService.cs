using uSLearn.Core.EventBus.Events;

namespace uSLearn.Accounts.Application.IntegrationEvents;

public interface IAccountIntegrationEventService
{
    Task PublishEventsThroughEventBusAsync(Guid transactionId);
    Task AddAndSaveEventAsync(IntegrationEvent evt);
}

using System.Threading.Tasks;

using uSLearn.EventBus.Events;

namespace uSLearn.EventBus.Abstractions;

public interface IEventBus
{
    Task PublishAsync(IntegrationEvent @event);
}

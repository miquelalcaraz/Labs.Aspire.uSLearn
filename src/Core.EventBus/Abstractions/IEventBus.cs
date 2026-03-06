using System.Threading.Tasks;

using uSLearn.Core.EventBus.Events;

namespace uSLearn.Core.EventBus.Abstractions;

public interface IEventBus
{
    Task PublishAsync(IntegrationEvent @event);
}

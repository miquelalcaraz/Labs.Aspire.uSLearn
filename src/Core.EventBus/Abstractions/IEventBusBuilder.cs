using Microsoft.Extensions.DependencyInjection;

namespace uSLearn.Core.EventBus.Abstractions;


public interface IEventBusBuilder
{
    public IServiceCollection Services { get; }
}

using uSLearn.Core.EventBus.Abstractions;
using uSLearn.Core.EventBus.Extensions;
using uSLearn.Core.EventBusRabbitMQ;
using uSLearn.Identity.IntegrationEvents.EventHandling;
using uSLearn.Identity.IntegrationEvents.Events;

namespace uSLearn.Identity.Extensions
{
    public static class Extensions
    {
        public static void AddApplicationServices(this IHostApplicationBuilder builder)
        {
            var services = builder.Services;

            builder.AddRabbitMqEventBus("eventbus")
                    .AddEventBusSubscriptions();

            services.AddHttpContextAccessor();


        }

        private static void AddEventBusSubscriptions(this IEventBusBuilder eventBus)
        {
            eventBus.AddSubscription<OrganizationCreatedIntegrationEvent, OrganizationCreatedIntegrationEventHandler>();
        }
    }
}

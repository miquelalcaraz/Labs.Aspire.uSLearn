using MediatR;

using uSLearn.Core.Application.Telemetry;
using uSLearn.Core.Domain.SeedWork;

namespace uSLearn.Accounts.Infrastructure.Extensions
{
    static class MediatorExtension
    {
        public static async Task DispatchDomainEventsAsync(this IMediator mediator, AccountContext ctx)
        {
            var domainEntities = ctx.ChangeTracker
                .Entries<Entity>()
                .Where(x => x.Entity.DomainEvents != null && x.Entity.DomainEvents.Any());

            var domainEvents = domainEntities
                .SelectMany(x => x.Entity.DomainEvents)
                .ToList();

            domainEntities.ToList()
                .ForEach(entity => entity.Entity.ClearDomainEvents());

            foreach (var domainEvent in domainEvents)
            {
                var eventType = domainEvent.GetType().Name;

                // Record domain event publication metric
                ApplicationDiagnostics.DomainEventsPublished.Add(1,
                    new KeyValuePair<string, object?>("event_type", eventType));

                await mediator.Publish(domainEvent);
            }
        }
    }
}

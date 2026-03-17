using Microsoft.EntityFrameworkCore;

using uSLearn.Core.Application.Abstractions;
using uSLearn.Core.Application.Behaviors;
using uSLearn.Core.EventBus.Abstractions;
using uSLearn.Core.EventBus.Extensions;
using uSLearn.Core.EventBusRabbitMQ;
using uSLearn.Core.Infrastructure.Extensions;
using uSLearn.Core.Infrastructure.Http;
using uSLearn.Core.IntegrationEventLogEF.Services;
using uSLearn.Identity.Infrastructure;
using uSLearn.Identity.IntegrationEvents.EventHandling;
using uSLearn.Identity.IntegrationEvents.Events;
using uSLearn.Identity.Repositories;
using uSLearn.Identity.Services;

namespace uSLearn.Identity.Extensions
{
    public static class Extensions
    {
        public static void AddApplicationServices(this IHostApplicationBuilder builder)
        {
            var services = builder.Services;

            // Add database context
            services.AddDbContext<IdentityContext>(options =>
            {
                var connectionString = builder.Configuration.GetConnectionString("identitydb");
                options.UseSqlServer(connectionString, sqlOptions =>
                {
                    sqlOptions.MigrationsAssembly(typeof(Program).Assembly.FullName);
                    // Retry transient SQL errors (deadlocks, timeouts); required by ResilientTransaction
                    sqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
                });
            });
            services.AddMigration<IdentityContext, IdentityContextSeed>();
            builder.AddRabbitMqEventBus("eventbus")
                    .AddEventBusSubscriptions();

            // Register HTTP context accessor and request context abstraction
            services.AddHttpContextAccessor();
            services.AddScoped<IRequestContextAccessor, HttpRequestContextAccessor>();

            // Add MediatR with LoggingBehavior (when Identity has commands/queries)
            // services.AddMediatR(cfg =>
            // {
            //     cfg.RegisterServicesFromAssemblyContaining(typeof(Program));
            //     cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            // });

            // Register repositories (EF Core-based)
            services.AddScoped<ITenantRepository, TenantRepository>();
            services.AddScoped<IUserRepository, UserRepository>();

            // Register services
            services.AddSingleton<IPasswordGenerator, PasswordGenerator>();

            // Register idempotency service
            services.AddScoped<IEventIdempotencyService, EventIdempotencyService<IdentityContext>>();
        }

        private static void AddEventBusSubscriptions(this IEventBusBuilder eventBus)
        {
            eventBus.AddSubscription<OrganizationCreatedIntegrationEvent, OrganizationCreatedIntegrationEventHandler>();
        }
    }
}

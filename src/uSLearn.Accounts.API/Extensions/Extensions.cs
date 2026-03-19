using FluentValidation;

using Microsoft.EntityFrameworkCore;

using uSLearn.Accounts.Application.IntegrationEvents;
using uSLearn.Accounts.Application.Queries;
using uSLearn.Accounts.Domain.OrganizationAggregate;
using uSLearn.Accounts.Infrastructure;
using uSLearn.Accounts.Infrastructure.Idempotency;
using uSLearn.Accounts.Infrastructure.Repositories;
using uSLearn.Accounts.Infrastructure.Seed;
using uSLearn.Core.Application.Abstractions;
using uSLearn.Core.Application.Behaviors;
using uSLearn.Core.EventBus.Abstractions;
using uSLearn.Core.EventBusRabbitMQ;
using uSLearn.Core.Infrastructure.Extensions;
using uSLearn.Core.Infrastructure.Http;
using uSLearn.Core.IntegrationEventLogEF.Services;

namespace uSLearn.Accounts.Extensions
{
    public static class Extensions
    {
        public static void AddApplicationServices(this IHostApplicationBuilder builder)
        {
            var services = builder.Services;

            var connectionString = builder.Configuration.GetConnectionString("accountdb");
            services.AddDbContext<AccountContext>(options =>
            {
                var connectionString = builder.Configuration.GetConnectionString("accountdb");

                options.UseSqlServer(connectionString, options =>
                {
                    options.MigrationsAssembly(typeof(Program).Assembly.FullName);
                });

            });
            services.AddMigration<AccountContext, AccountContextSeed>();

            // Add the integration services that consume the DbContext
            services.AddTransient<IIntegrationEventLogService, IntegrationEventLogService<AccountContext>>();
            services.AddTransient<IAccountIntegrationEventService, AccountIntegrationEventService>();

            builder.AddRabbitMqEventBus("eventbus")
                .AddEventBusSubscriptions();

            // Register HTTP context accessor and request context abstraction
            services.AddHttpContextAccessor();
            services.AddScoped<IRequestContextAccessor, HttpRequestContextAccessor>();

            // Register FluentValidation validators
            services.AddValidatorsFromAssemblyContaining<Program>();

            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssemblyContaining(typeof(Program));

                // Order matters: Logging → Validation → Transaction → Handler
                cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));       // 1. Observability
                cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));    // 2. Validation (early exit)
                cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));   // 3. Transaction (only if valid)
            });

            services.AddScoped<IOrganizationQueries, OrganizationQueries>();
            services.AddScoped<IOrganizationRepository, OrganizationRepository>();
            services.AddScoped<IRequestManager, RequestManager>();

        }

        private static void AddEventBusSubscriptions(this IEventBusBuilder eventBus)
        {
        }
    }
}

using Microsoft.EntityFrameworkCore;

using uSLearn.Accounts.Application.Behaviors;
using uSLearn.Accounts.Application.IntegrationEvents;
using uSLearn.Accounts.Application.Queries;
using uSLearn.Accounts.Domain.OrganizationAggregate;
using uSLearn.Accounts.Infrastructure;
using uSLearn.Accounts.Infrastructure.Idempotency;
using uSLearn.Accounts.Infrastructure.Repositories;
using uSLearn.Accounts.Infrastructure.Seed;
using uSLearn.Core.EventBus.Abstractions;
using uSLearn.Core.EventBusRabbitMQ;
using uSLearn.Core.Infrastructure.Extensions;
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

            services.AddHttpContextAccessor();

            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssemblyContaining(typeof(Program));
                cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
                cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
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

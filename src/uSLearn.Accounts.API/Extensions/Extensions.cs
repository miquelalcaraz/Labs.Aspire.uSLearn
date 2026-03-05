using Microsoft.EntityFrameworkCore;

using uSLearn.Accounts.API.Application.Behaviors;
using uSLearn.Accounts.API.Application.Queries;
using uSLearn.Accounts.API.Domain.OrganizationAggregate;
using uSLearn.Accounts.API.Infrastructure;
using uSLearn.Accounts.API.Infrastructure.Extensions;
using uSLearn.Accounts.API.Infrastructure.Repositories;
using uSLearn.Accounts.API.Infrastructure.Seed;

namespace uSLearn.Accounts.API.Extensions
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

            builder.AddRabbitMqEventBus("eventbus")
                .AddEventBusSubscriptions();

            services.AddHttpContextAccessor();

            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssemblyContaining(typeof(Program));
                cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            });

            services.AddScoped<IOrganizationQueries, OrganizationQueries>();
            services.AddScoped<IOrganizationRepository, OrganizationRepository>();
     
        }

        private static void AddEventBusSubscriptions(this IEventBusBuilder eventBus)
        {
        }
    }
}

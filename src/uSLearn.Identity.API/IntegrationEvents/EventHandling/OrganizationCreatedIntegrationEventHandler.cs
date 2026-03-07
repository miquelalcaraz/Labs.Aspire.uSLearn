using uSLearn.Core.EventBus.Abstractions;
using uSLearn.Core.IntegrationEventLogEF.Services;
using uSLearn.Identity.IntegrationEvents.Events;
using uSLearn.Identity.Models;
using uSLearn.Identity.Repositories;
using uSLearn.Identity.Services;

namespace uSLearn.Identity.IntegrationEvents.EventHandling
{
    public class OrganizationCreatedIntegrationEventHandler : IIntegrationEventHandler<OrganizationCreatedIntegrationEvent>
    {
        private readonly ILogger<OrganizationCreatedIntegrationEventHandler> _logger;
        private readonly ITenantRepository _tenantRepository;
        private readonly IUserRepository _userRepository;
        private readonly IPasswordGenerator _passwordGenerator;
        private readonly IEventIdempotencyService _idempotencyService;

        public OrganizationCreatedIntegrationEventHandler(
            ILogger<OrganizationCreatedIntegrationEventHandler> logger,
            ITenantRepository tenantRepository,
            IUserRepository userRepository,
            IPasswordGenerator passwordGenerator,
            IEventIdempotencyService idempotencyService)
        {
            _logger = logger;
            _tenantRepository = tenantRepository;
            _userRepository = userRepository;
            _passwordGenerator = passwordGenerator;
            _idempotencyService = idempotencyService;
        }

        public async Task Handle(OrganizationCreatedIntegrationEvent @event)
        {
            var handlerName = GetType().Name;

            _logger.LogInformation(
                "Identity - Processing organization created event: {EventId} - {OrganizationId} - {Name}",
                @event.Id,
                @event.OrganizationId,
                @event.Name);

            // Check if event already processed
            if (await _idempotencyService.IsProcessedAsync(@event.Id, handlerName))
            {
                _logger.LogWarning(
                    "Identity - Event {EventId} already processed. Skipping duplicate.",
                    @event.Id);
                return;
            }

            // Create Tenant
            var tenant = await CreateTenantAsync(@event);

            // Create Admin User
            var adminUser = await CreateAdminUserAsync(@event, tenant.Id);

            // Mark as processed
            await _idempotencyService.MarkAsProcessedAsync(
                @event.Id,
                handlerName,
                @event.GetType().Name);

            _logger.LogInformation(
                "Identity - Tenant {TenantId} and Admin User {UserId} created for organization {OrganizationId}",
                tenant.Id, adminUser.Id, @event.OrganizationId);
        }

        private async Task<Tenant> CreateTenantAsync(OrganizationCreatedIntegrationEvent @event)
        {
            var tenant = new Tenant
            {
                Id = @event.TenantId,
                OrganizationId = @event.OrganizationId,
                Name = @event.Name,
                CreatedAt = DateTime.UtcNow
            };

            return await _tenantRepository.CreateAsync(tenant);
        }

        private async Task<AdminUser> CreateAdminUserAsync(OrganizationCreatedIntegrationEvent @event, Guid tenantId)
        {
            var adminUser = new AdminUser
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Email = GenerateAdminEmail(@event.Name),
                TemporaryPassword = _passwordGenerator.GenerateTemporaryPassword(),
                CreatedAt = DateTime.UtcNow
            };

            return await _userRepository.CreateAsync(adminUser);
        }

        private static string GenerateAdminEmail(string organizationName)
        {
            var sanitizedName = organizationName.ToLower()
                .Replace(" ", "")
                .Replace(".", "")
                .Replace(",", "");

            return $"admin@{sanitizedName}.com";
        }
    }
}

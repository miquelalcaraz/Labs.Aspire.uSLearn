using System.Collections.Concurrent;

using uSLearn.Identity.Models;

namespace uSLearn.Identity.Repositories
{
    public class InMemoryTenantRepository : ITenantRepository
    {
        private readonly ConcurrentDictionary<Guid, Tenant> _tenants = new();

        public Task<Tenant> CreateAsync(Tenant tenant)
        {
            _tenants[tenant.Id] = tenant;
            return Task.FromResult(tenant);
        }

        public Task<Tenant?> GetByIdAsync(Guid id)
        {
            _tenants.TryGetValue(id, out var tenant);
            return Task.FromResult(tenant);
        }

        public Task<Tenant?> GetByOrganizationIdAsync(Guid organizationId)
        {
            var tenant = _tenants.Values.FirstOrDefault(t => t.OrganizationId == organizationId);
            return Task.FromResult(tenant);
        }
    }
}

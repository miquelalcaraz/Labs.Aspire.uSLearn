using uSLearn.Identity.Models;

namespace uSLearn.Identity.Repositories
{
    public interface ITenantRepository
    {
        Task<Tenant> CreateAsync(Tenant tenant);
        Task<Tenant?> GetByIdAsync(Guid id);
        Task<Tenant?> GetByOrganizationIdAsync(Guid organizationId);
    }
}

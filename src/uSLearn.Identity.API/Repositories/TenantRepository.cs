using Microsoft.EntityFrameworkCore;
using uSLearn.Identity.Infrastructure;
using uSLearn.Identity.Models;

namespace uSLearn.Identity.Repositories;

public class TenantRepository : ITenantRepository
{
    private readonly IdentityContext _context;

    public TenantRepository(IdentityContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Tenant> CreateAsync(Tenant tenant)
    {
        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync();
        return tenant;
    }

    public async Task<Tenant?> GetByIdAsync(Guid id)
    {
        return await _context.Tenants
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<Tenant?> GetByOrganizationIdAsync(Guid organizationId)
    {
        return await _context.Tenants
            .FirstOrDefaultAsync(t => t.OrganizationId == organizationId);
    }
}

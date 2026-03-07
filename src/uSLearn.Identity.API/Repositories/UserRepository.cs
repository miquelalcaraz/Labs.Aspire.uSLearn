using Microsoft.EntityFrameworkCore;
using uSLearn.Identity.Infrastructure;
using uSLearn.Identity.Models;

namespace uSLearn.Identity.Repositories;

public class UserRepository : IUserRepository
{
    private readonly IdentityContext _context;

    public UserRepository(IdentityContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<AdminUser> CreateAsync(AdminUser user)
    {
        _context.AdminUsers.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    public async Task<AdminUser?> GetByIdAsync(Guid id)
    {
        return await _context.AdminUsers
            .FirstOrDefaultAsync(u => u.Id == id);
    }

    public async Task<IEnumerable<AdminUser>> GetByTenantIdAsync(Guid tenantId)
    {
        return await _context.AdminUsers
            .Where(u => u.TenantId == tenantId)
            .ToListAsync();
    }
}

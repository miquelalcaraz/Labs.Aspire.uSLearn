using System.Collections.Concurrent;

using uSLearn.Identity.Models;

namespace uSLearn.Identity.Repositories
{
    public class InMemoryUserRepository : IUserRepository
    {
        private readonly ConcurrentDictionary<Guid, AdminUser> _users = new();

        public Task<AdminUser> CreateAsync(AdminUser user)
        {
            _users[user.Id] = user;
            return Task.FromResult(user);
        }

        public Task<AdminUser?> GetByIdAsync(Guid id)
        {
            _users.TryGetValue(id, out var user);
            return Task.FromResult(user);
        }

        public Task<IEnumerable<AdminUser>> GetByTenantIdAsync(Guid tenantId)
        {
            var users = _users.Values.Where(u => u.TenantId == tenantId);
            return Task.FromResult(users);
        }
    }
}

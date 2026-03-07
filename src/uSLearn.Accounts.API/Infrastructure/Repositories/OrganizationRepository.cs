using uSLearn.Accounts.Domain.OrganizationAggregate;
using uSLearn.Core.Domain.SeedWork;

namespace uSLearn.Accounts.Infrastructure.Repositories
{
    public class OrganizationRepository : IOrganizationRepository
    {
        private readonly AccountContext _context;

        public IUnitOfWork UnitOfWork => _context;

        public OrganizationRepository(AccountContext context)
        {
            _context = context;
        }

        public Organization Add(Organization Organization)
        {
            return _context.Organizations.Add(Organization).Entity;
        }

        public async Task<Organization> GetAsync(Guid OrganizationId)
        {
            var organization = await _context.Organizations.FindAsync(OrganizationId);

            return organization!;
        }

        public void Update(Organization Organization)
        {
            _context.Entry(Organization).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
        }
    }
}

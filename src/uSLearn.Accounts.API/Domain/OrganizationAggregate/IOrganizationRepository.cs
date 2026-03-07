using uSLearn.Core.Domain.SeedWork;

namespace uSLearn.Accounts.Domain.OrganizationAggregate
{
    public interface IOrganizationRepository : IRepository<Organization>
    {
        Organization Add(Organization Organization);

        void Update(Organization Organization);

        Task<Organization> GetAsync(Guid OrganizationId);
    }
}

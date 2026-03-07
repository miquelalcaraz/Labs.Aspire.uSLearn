namespace uSLearn.Accounts.Application.Queries;

public interface IOrganizationQueries
{
    Task<Organization?> GetOrganizationAsync(Guid id);
    Task<IEnumerable<OrganizationSummary>> GetAllOrganizationsAsync();

}

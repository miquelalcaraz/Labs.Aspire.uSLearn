using Microsoft.EntityFrameworkCore;

using uSLearn.Accounts.Infrastructure;
namespace uSLearn.Accounts.Application.Queries;


public class OrganizationQueries(AccountContext context) : IOrganizationQueries
{
    public async Task<Organization?> GetOrganizationAsync(Guid id)
    {
        var organization = await context.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id);

        if (organization is null)
            throw new KeyNotFoundException(nameof(organization));

        return new Organization
        {
            Id = organization.Id,
            TenantId = organization.TenantId,
            OrganizationType = organization.OrganizationType.ToString(),
            Name = organization.Name,
            LegalName = organization.LegalName,
            TaxNumber = organization.TaxNumber,
            TaxNumberType = organization.TaxNumberType.ToString(),
            Language = organization.Language ?? string.Empty,
            LanguageCode = organization.LanguageCode ?? string.Empty,
            CurrencyCode = organization.CurrencyCode ?? string.Empty,
            Address = new AddressViewModel
            {
                Country = organization.Address?.Country ?? string.Empty,
                CountryCode = organization.Address?.CountryCode ?? string.Empty,
                State = organization.Address?.State ?? string.Empty,
                City = organization.Address?.City ?? string.Empty,
                Street = organization.Address?.Street ?? string.Empty,
                PostalCode = organization.Address?.PostalCode ?? string.Empty
            }
        };

    }

    public async Task<IEnumerable<OrganizationSummary>> GetAllOrganizationsAsync()
    {
        var organizations = await context.Organizations
            .AsNoTracking()
            .Select(o => new OrganizationSummary
            {
                Id = o.Id,
                TenantId = o.TenantId,
                OrganizationType = o.OrganizationType.ToString(),
                Name = o.Name,
                LegalName = o.LegalName,
                TaxNumber = o.TaxNumber,
                TaxNumberType = o.TaxNumberType.ToString(),
                Country = o.Address != null ? o.Address.Country : string.Empty,
                CountryCode = o.Address != null ? o.Address.CountryCode : string.Empty
            }).ToListAsync();

        return organizations;
    }


}

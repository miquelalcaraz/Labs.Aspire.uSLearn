namespace uSLearn.Accounts.API.Application.Queries;


public record Organization
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string OrganizationType { get; init; } = null!;
    public string Name { get; init; } = null!;
    public string LegalName { get; init; } = null!;
    public string TaxNumber { get; init; } = null!;
    public string TaxNumberType { get; init; } = null!;
    public string Language { get; init; } = string.Empty;
    public string LanguageCode { get; init; } = string.Empty;
    public string CurrencyCode { get; init; } = string.Empty;
    public AddressViewModel Address { get; init; } = null!;
}


public record OrganizationSummary
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string OrganizationType { get; init; } = null!;
    public string Name { get; init; } = null!;
    public string LegalName { get; init; } = null!;
    public string TaxNumber { get; init; } = null!;
    public string TaxNumberType { get; init; } = null!;
    public string Country { get; init; } = string.Empty;
    public string CountryCode { get; init; } = string.Empty;
}


public record AddressViewModel
{
    public string Country { get; init; } = string.Empty;
    public string CountryCode { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string Street { get; init; } = string.Empty;
    public string PostalCode { get; init; } = string.Empty;
}
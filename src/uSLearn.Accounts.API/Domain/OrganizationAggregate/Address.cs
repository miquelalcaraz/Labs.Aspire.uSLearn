using uSLearn.Accounts.API.Domain.SeedWork;

namespace uSLearn.Accounts.API.Domain.OrganizationAggregate;

public class Address : ValueObject
{
    public string Country { get; private set; } = null!;
    public string CountryCode { get; internal set; } = null!;
    public string? State { get; private set; } = null!;
    public string? PostalCode { get; private set; } = null!;
    public string City { get; private set; } = null!;
    public string? Street { get; private set; } = null!;
     
    protected Address() { }

    public static Address Create(string country, string countryCode, string? state, string? postalCode, string city, string? street)
    {
        return new Address
        {
            Street = street,
            City = city,
            State = state,
            Country = country,
            PostalCode = postalCode,
            CountryCode = countryCode
        };
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Street ?? string.Empty;
        yield return City;
        yield return State ?? string.Empty;
        yield return Country;
        yield return PostalCode ?? string.Empty;
        yield return CountryCode;
    }
}

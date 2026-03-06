using System.Runtime.Serialization;

using MediatR;

using uSLearn.Accounts.Domain.OrganizationAggregate;



namespace uSLearn.Accounts.Application.Commands;

[DataContract]
public record CreateOrganizationCommand : IRequest<bool>
{
    [DataMember]
    public OrganizationType OrganizationType { get; private set; } = OrganizationType.Company;

    [DataMember]
    public string Name { get; private set; } = null!;

    [DataMember]
    public string LegalName { get; private set; } = null!;

    [DataMember]
    public string TaxIdNumber { get; private set; } = null!;

    [DataMember]
    public TaxNumberType TaxNumberType { get; private set; }

    [DataMember]
    public string Country { get; private set; } = null!;

    [DataMember]
    public string CountryCode { get; private set; } = null!;

    [DataMember]
    public string? State { get; private set; } = null!;

    [DataMember]
    public string ZipCode { get; private set; } = null!;

    [DataMember]
    public string? City { get; private set; } = null!;

    [DataMember]
    public string? Street { get; private set; } = null!;

    [DataMember]
    public string Language { get; private set; } = null!;

    [DataMember]
    public string LanguageCode { get; private set; } = null!;



    public CreateOrganizationCommand() { }

    public CreateOrganizationCommand(
        string taxIdNumber,
        string name,
        string legalName,
        string street,
        string city,
        string state,
        string country,
        string zipCode,
        OrganizationType organizationType = OrganizationType.Company,
        TaxNumberType taxNumberType = TaxNumberType.TaxId,
        string? countryCode = null,
        string? language = null,
        string? languageCode = null)
    {
        TaxIdNumber = taxIdNumber;
        Name = name;
        LegalName = legalName;
        Street = street;
        City = city;
        State = state;
        Country = country;
        ZipCode = zipCode;
        OrganizationType = organizationType;
        TaxNumberType = taxNumberType;
        CountryCode = countryCode ?? "US";
        Language = language ?? "English";
        LanguageCode = languageCode ?? "en";
    }
}

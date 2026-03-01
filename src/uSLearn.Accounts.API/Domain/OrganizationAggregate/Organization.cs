using uSLearn.Accounts.API.Domain.Events;
using uSLearn.Accounts.API.Domain.Exceptions;
using uSLearn.Accounts.API.Domain.SeedWork;

namespace uSLearn.Accounts.API.Domain.OrganizationAggregate;

public class Organization : Entity, IAggregateRoot
{
    public Guid TenantId { get; private set; }

    public OrganizationType OrganizationType { get; private set; }

    public string Name { get; private set; } = null!;

    public string LegalName { get; private set; } = null!;

    public Address? Address { get; private set; } = null!;

    public string TaxNumber { get; private set; } = null!;

    public TaxNumberType TaxNumberType { get; private set; }

    public string? Language { get; private set; } = null!;

    public string? LanguageCode { get; private set; } = null!;

    public string? CurrencyCode { get; private set; } = null!;



    private readonly List<OrganizationContact> _organizationContacts;
    public IReadOnlyCollection<OrganizationContact> OrganizationContacts => _organizationContacts.AsReadOnly();



    protected Organization()
    {
        _organizationContacts = new List<OrganizationContact>();
        Id = Guid.NewGuid();
    }

    public static Organization Create(string name, string legalName, Address address, string taxNumber, TaxNumberType taxNumberType, OrganizationType organizationType = OrganizationType.Company, string language = null!, string languageCode = null!, string currencyCode = null!)
    {
        if (taxNumberType == TaxNumberType.Unknown)
        {
            throw new AccountDomainException("TaxNumberType cannot be Unknown");
        }

        var organization = new Organization()
        {
            TenantId = Guid.NewGuid(),
            Name = name.Trim(),
            LegalName = legalName.Trim(),
            TaxNumber = taxNumber.Trim(),
            TaxNumberType = taxNumberType,
            Language = language,
            LanguageCode = languageCode,
            CurrencyCode = currencyCode,
            OrganizationType = organizationType,
            Address = address

        };

        organization.AddOrganizationCreatedDomainEvent();
        return organization;
    }

    private void AddOrganizationCreatedDomainEvent()
    {
        var organizationCreatedDomainEvent = new OrganizationCreatedDomainEvent(TenantId, Id, Name, LegalName, TaxNumber!, Address?.CountryCode!);
        this.AddDomainEvent(organizationCreatedDomainEvent);
    }

    public void AddOrganizationContact(string firstName, string lastName, string email, string phoneNumber, string jobtitle)
    {
        var existingContactForOrganization = _organizationContacts.SingleOrDefault(o => o.Email.Equals(email, StringComparison.OrdinalIgnoreCase) || o.PhoneNumber.Equals(phoneNumber, StringComparison.OrdinalIgnoreCase));

        if (existingContactForOrganization is null)
        {
            var newContact = OrganizationContact.Create(Id, firstName, lastName, email, phoneNumber, jobtitle);
            _organizationContacts.Add(newContact);
        }

    }

}

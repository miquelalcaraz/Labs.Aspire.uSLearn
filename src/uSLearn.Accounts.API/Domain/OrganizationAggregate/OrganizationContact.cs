using System.ComponentModel.DataAnnotations;

using uSLearn.Core.Domain.SeedWork;

namespace uSLearn.Accounts.Domain.OrganizationAggregate;

public partial class OrganizationContact : Entity
{

    [Required]
    public Guid OrganizationId { get; private set; }
    [Required]
    public string FirstName { get; private set; } = null!;

    public string? LastName { get; private set; } = null!;
    [Required]
    public string Email { get; private set; } = null!;
    [Required]
    public string PhoneNumber { get; private set; } = null!;

    public string? JobTitle { get; private set; } = null!;

    public Organization Organization { get; } = null!;

    protected OrganizationContact()
    {
        Id = Guid.NewGuid();
    }

    public static OrganizationContact Create(Guid organizationId, string firstName, string lastName, string email, string phoneNumber, string jobtitle)
    {
        return new OrganizationContact()
        {

            OrganizationId = organizationId,
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            PhoneNumber = phoneNumber,
            JobTitle = jobtitle
        };
    }

}
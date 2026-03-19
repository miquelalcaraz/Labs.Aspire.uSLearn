using FluentValidation;

using uSLearn.Accounts.Application.Commands;
using uSLearn.Accounts.Domain.OrganizationAggregate;

namespace uSLearn.Accounts.Application.Validators;

/// <summary>
/// Validator for CreateOrganizationCommand.
/// Validates business rules before the command reaches the domain layer.
/// </summary>
public class CreateOrganizationCommandValidator : AbstractValidator<CreateOrganizationCommand>
{
    public CreateOrganizationCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Organization name is required")
            .MaximumLength(200).WithMessage("Organization name cannot exceed 200 characters");

        RuleFor(x => x.LegalName)
            .NotEmpty().WithMessage("Legal name is required")
            .MaximumLength(250).WithMessage("Legal name cannot exceed 250 characters");

        RuleFor(x => x.TaxIdNumber)
            .NotEmpty().WithMessage("Tax ID number is required")
            .MaximumLength(50).WithMessage("Tax ID number cannot exceed 50 characters")
            .Matches(@"^[A-Za-z0-9\-]+$").WithMessage("Tax ID number can only contain alphanumeric characters and hyphens");

        RuleFor(x => x.TaxNumberType)
            .NotEqual(TaxNumberType.Unknown).WithMessage("Tax number type must be specified")
            .IsInEnum().WithMessage("Invalid tax number type");

        RuleFor(x => x.Country)
            .NotEmpty().WithMessage("Country is required")
            .MaximumLength(100).WithMessage("Country cannot exceed 100 characters");

        RuleFor(x => x.ZipCode)
            .NotEmpty().WithMessage("Zip code is required")
            .MaximumLength(20).WithMessage("Zip code cannot exceed 20 characters");

        RuleFor(x => x.Street)
            .MaximumLength(250).WithMessage("Street cannot exceed 250 characters")
            .When(x => !string.IsNullOrEmpty(x.Street));

        RuleFor(x => x.City)
            .MaximumLength(100).WithMessage("City cannot exceed 100 characters")
            .When(x => !string.IsNullOrEmpty(x.City));

        RuleFor(x => x.State)
            .MaximumLength(100).WithMessage("State cannot exceed 100 characters")
            .When(x => !string.IsNullOrEmpty(x.State));

        RuleFor(x => x.OrganizationType)
            .IsInEnum().WithMessage("Invalid organization type");
    }
}

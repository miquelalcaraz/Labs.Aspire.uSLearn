
using MediatR;

using uSLearn.Accounts.Domain.OrganizationAggregate;
using uSLearn.Accounts.Infrastructure.Idempotency;




namespace uSLearn.Accounts.Application.Commands;

public class CreateOrganizationCommandHandler : IRequestHandler<CreateOrganizationCommand, bool>
{
    private readonly IMediator _mediator;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly ILogger<CreateOrganizationCommandHandler> _logger;
    public CreateOrganizationCommandHandler(IMediator mediator, IOrganizationRepository organizationRepository, ILogger<CreateOrganizationCommandHandler> logger)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _organizationRepository = organizationRepository ?? throw new ArgumentNullException(nameof(organizationRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> Handle(CreateOrganizationCommand message, CancellationToken cancellationToken)
    {
        var address = Address.Create(message.Country, message.CountryCode, message.State, message.ZipCode, message.City!, message.Street);
        var organization = Organization.Create(message.Name, message.LegalName, address, message.TaxIdNumber, message.TaxNumberType, message.OrganizationType, message.Language, message.LanguageCode);
        _organizationRepository.Add(organization);
        return await _organizationRepository.UnitOfWork.SaveEntitiesAsync(cancellationToken);
    }



}

// Use for Idempotency in Command process
public class CreateOrganizationIdentifiedCommandHandler : IdentifiedCommandHandler<CreateOrganizationCommand, bool>
{
    public CreateOrganizationIdentifiedCommandHandler(
        IMediator mediator,
        IRequestManager requestManager,
        ILogger<IdentifiedCommandHandler<CreateOrganizationCommand, bool>> logger)
        : base(mediator, requestManager, logger)
    {
    }

    protected override bool CreateResultForDuplicateRequest()
    {
        return true; // Ignore duplicate requests for processing order.
    }
}
public record OrganizationCreatedDTO
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string Name { get; init; } = null!;
    public string LegalName { get; init; } = null!;
    public string? TaxIdNumber { get; init; } = null!;

    public string Country { get; init; } = null!;
    public string Language { get; init; } = null!;
    public string CountryCode { get; init; } = null!;
    public string LanguageCode { get; init; } = null!;


}
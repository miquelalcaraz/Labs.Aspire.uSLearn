using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

using uSLearn.Accounts.Application.Commands;
using uSLearn.Accounts.Application.Queries;
using uSLearn.Accounts.Domain.OrganizationAggregate;
using uSLearn.Accounts.Infrastructure.Extensions;

using OrganizationViewModel = uSLearn.Accounts.Application.Queries.Organization;


namespace uSLearn.Accounts.Api
{
    public static class AccountsApi
    {
        public static RouteGroupBuilder MapAccountsApiV1(this IEndpointRouteBuilder app)
        {
            var api = app.MapGroup("api/accounts").HasApiVersion(1.0);
            api.MapPut("/", CreateOrganizationAsync);
            api.MapGet("/{id:guid}", GetOrganizationByIdAsync);
            api.MapGet("/", GetAllOrganizationsAsync);
            return api;
        }

        public static async Task<Results<Ok, BadRequest<string>>> CreateOrganizationAsync(
            [FromHeader(Name = "x-requestid")] Guid requestId,
            CreateOrganizationRequest request,
            [AsParameters] AccountsServices services)
        {
            services.Logger.LogInformation(
                "Sending command: {CommandName} - {IdProperty}: {CommandId}",
                request.GetGenericTypeName(),
                nameof(request.TaxIdNumber),
                request.TaxIdNumber); //don't log the request as it has CC number

            if (requestId == Guid.Empty)
            {
                services.Logger.LogWarning("Invalid IntegrationEvent - RequestId is missing - {@IntegrationEvent}", request);
                return TypedResults.BadRequest("Empty GUID is not valid for request ID");
            }


            using (services.Logger.BeginScope(new List<KeyValuePair<string, object>> { new("IdentifiedCommandId", requestId) }))
            {
                var createOrganizationCommand = new CreateOrganizationCommand(
                request.TaxIdNumber,
                request.Name,
                request.LegalName,
                request.Street,
                request.City,
                request.State,
                request.Country,
                request.ZipCode,
                request.OrganizationType,
                request.TaxNumberType);

                var identifiedCommand = new IdentifiedCommand<CreateOrganizationCommand, bool>(createOrganizationCommand, requestId);

                var result = await services.Mediator.Send(identifiedCommand);

                services.Logger.LogInformation(
                        "Sending command: {CommandName} - {IdProperty}: {CommandId} ({@Command})",
                        identifiedCommand.GetGenericTypeName(),
                        nameof(identifiedCommand.Id),
                        identifiedCommand.Id,
                        identifiedCommand);

                if (result)
                {
                    services.Logger.LogInformation("CreateOrderCommand succeeded - RequestId: {RequestId}", requestId);
                }
                else
                {
                    services.Logger.LogWarning("CreateOrderCommand failed - RequestId: {RequestId}", requestId);
                    return TypedResults.BadRequest("Failed to create organization");
                }

                return TypedResults.Ok();
            }
        }

        public static async Task<Results<Ok<OrganizationViewModel>, NotFound>> GetOrganizationByIdAsync(
            Guid id,
            [AsParameters] AccountsServices services)
        {
            try
            {
                var organization = await services.Queries.GetOrganizationAsync(id);

                if (organization is null)
                {
                    return TypedResults.NotFound();
                }

                return TypedResults.Ok(organization);
            }
            catch (KeyNotFoundException)
            {
                return TypedResults.NotFound();
            }
        }

        public static async Task<Ok<IEnumerable<OrganizationSummary>>> GetAllOrganizationsAsync(
            [AsParameters] AccountsServices services)
        {
            var organizations = await services.Queries.GetAllOrganizationsAsync();
            return TypedResults.Ok(organizations);
        }
    }


    public record CreateOrganizationRequest
    (
        string TaxIdNumber,
        TaxNumberType TaxNumberType,
        string Name,
        string LegalName,
        string Street,
        string City,
        string State,
        string Country,
        string ZipCode,
        OrganizationType OrganizationType
    );

}

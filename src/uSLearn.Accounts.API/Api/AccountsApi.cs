using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

using uSLearn.Accounts.API.Application.Commands;
using uSLearn.Accounts.API.Application.Queries;
using uSLearn.Accounts.API.Domain.OrganizationAggregate;

using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

using OrganizationViewModel = uSLearn.Accounts.API.Application.Queries.Organization;


namespace uSLearn.Accounts.API.Api
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
            if (requestId == Guid.Empty)
            {
                return TypedResults.BadRequest("Empty GUID is not valid for request ID");
            }



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

            if (result)
            {
                return TypedResults.Ok();
            }
            else
            {
                return TypedResults.BadRequest("Failed to create organization");
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

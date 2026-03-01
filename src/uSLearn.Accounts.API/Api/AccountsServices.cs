using MediatR;

using uSLearn.Accounts.API.Application.Queries;


namespace uSLearn.Accounts.API.Api
{
    public class AccountsServices(
                    IMediator mediator,
                    ILogger<AccountsServices> logger,
                    IOrganizationQueries queries
        )
    {
        public IMediator Mediator { get; set; } = mediator;
        public ILogger<AccountsServices> Logger { get; } = logger;
        public IOrganizationQueries Queries { get; } = queries;
    }


}

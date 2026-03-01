using uSLearn.Accounts.API.Infrastructure;
using uSLearn.Accounts.API.Infrastructure.Extensions;

namespace uSLearn.Accounts.API.Infrastructure.Seed
{
    public class AccountContextSeed : IDbSeeder<AccountContext>
    {
        public async Task SeedAsync(AccountContext context)
        {
            // Seed initial data here
        }
    }
}

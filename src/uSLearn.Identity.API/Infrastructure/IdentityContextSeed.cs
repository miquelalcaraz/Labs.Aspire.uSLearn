using uSLearn.Core.Infrastructure.Extensions;

namespace uSLearn.Identity.Infrastructure
{
    public class IdentityContextSeed : IDbSeeder<IdentityContext>
    {
        public async Task SeedAsync(IdentityContext context)
        {
            // Seed initial data here
        }
    }
}

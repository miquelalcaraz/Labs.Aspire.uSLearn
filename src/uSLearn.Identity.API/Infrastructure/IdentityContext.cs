using Microsoft.EntityFrameworkCore;

using uSLearn.Core.IntegrationEventLogEF;

namespace uSLearn.Identity.Infrastructure;

public class IdentityContext : DbContext
{

    public IdentityContext(DbContextOptions<IdentityContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");
        modelBuilder.UseEventIdempotency();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using uSLearn.Accounts.Infrastructure.Idempotency;

namespace uSLearn.Accounts.Infrastructure.EntityConfigurations;

internal sealed class ClientRequestConfiguration : IEntityTypeConfiguration<ClientRequest>
{
    public void Configure(EntityTypeBuilder<ClientRequest> requestConfiguration)
    {
        requestConfiguration.ToTable("requests");
        requestConfiguration.HasKey(r => r.Id);
        requestConfiguration.HasIndex(r => r.Id).IsUnique();
    }
}

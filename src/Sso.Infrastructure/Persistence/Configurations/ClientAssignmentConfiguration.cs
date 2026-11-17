using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sso.Domain.Entities;

namespace Sso.Infrastructure.Persistence.Configurations;

public sealed class ClientAssignmentConfiguration : IEntityTypeConfiguration<ClientAssignment>
{
    public void Configure(EntityTypeBuilder<ClientAssignment> b)
    {
        b.ToTable("client_assignments");
        b.HasKey(a => a.Id);
        b.HasIndex(a => a.UserId);
        b.HasIndex(a => new { a.UserId, a.ClientId }).IsUnique();
        b.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(a => a.Client).WithMany().HasForeignKey(a => a.ClientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

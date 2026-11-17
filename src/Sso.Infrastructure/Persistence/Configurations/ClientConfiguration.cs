using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sso.Domain.Entities;

namespace Sso.Infrastructure.Persistence.Configurations;

public sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> b)
    {
        b.ToTable("clients");
        b.HasKey(c => c.Id);
        b.Property(c => c.ClientId).HasMaxLength(100);
        b.HasIndex(c => c.ClientId).IsUnique();
        b.Property(c => c.Name).HasMaxLength(200);
        b.Property(c => c.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(c => c.Description).HasMaxLength(500);
        b.Property(c => c.LaunchUrl).HasMaxLength(500);
    }
}

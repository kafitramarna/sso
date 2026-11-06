using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sso.Domain.Entities;

namespace Sso.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(u => u.Id);
        b.Property(u => u.Email).HasColumnType("citext").HasMaxLength(320);
        b.HasIndex(u => u.Email).IsUnique();
        b.Property(u => u.DisplayName).HasMaxLength(200);
        b.Property(u => u.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(u => u.LastLoginIp).HasMaxLength(45);
        b.Ignore(u => u.EmailVerified);
        b.Ignore(u => u.IsDisabled);
        b.HasMany(u => u.OneTimeTokens).WithOne(t => t.User!).HasForeignKey(t => t.UserId);
    }
}

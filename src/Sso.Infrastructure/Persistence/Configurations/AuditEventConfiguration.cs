using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sso.Domain.Entities;

namespace Sso.Infrastructure.Persistence.Configurations;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> b)
    {
        b.ToTable("audit_events");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedOnAdd();
        b.Property(e => e.Event).HasMaxLength(100);
        b.Property(e => e.Ip).HasColumnType("inet");
        b.Property(e => e.UserAgent).HasMaxLength(500);
        b.Property(e => e.Metadata).HasColumnType("jsonb");
        b.HasIndex(e => new { e.UserId, e.CreatedAt }).IsDescending(false, true);
    }
}

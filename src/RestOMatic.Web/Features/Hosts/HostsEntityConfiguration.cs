using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RestOMatic.Web.Features.Hosts;

internal sealed class HostConfiguration : IEntityTypeConfiguration<Host>
{
    public void Configure(EntityTypeBuilder<Host> host)
    {
        host.ToTable("Hosts");
        host.HasKey(h => h.Id);
        host.Property(h => h.Name).IsRequired();
        host.Property(h => h.NormalizedName).IsRequired();
        host.HasIndex(h => h.NormalizedName).IsUnique();
        host.HasIndex(h => h.EnrolTokenHash).IsUnique();
        host.HasIndex(h => h.CredentialHash).IsUnique();
        host.Ignore(h => h.IsEnrolled);

        host.HasMany(h => h.Parts)
            .WithOne()
            .HasForeignKey(p => p.HostId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class HostPartConfiguration : IEntityTypeConfiguration<HostPart>
{
    public void Configure(EntityTypeBuilder<HostPart> part)
    {
        part.ToTable("HostParts");
        part.HasKey(p => new { p.HostId, p.Kind });
        part.Property(p => p.Kind).IsRequired();
        part.Property(p => p.Fingerprint).IsRequired();
    }
}

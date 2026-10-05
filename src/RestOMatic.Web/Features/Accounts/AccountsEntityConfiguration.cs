using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RestOMatic.Web.Features.Accounts;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> user)
    {
        user.ToTable("Users");
        user.HasKey(u => u.Id);
        user.Property(u => u.UserName).IsRequired();
        user.Property(u => u.NormalizedUserName).IsRequired();
        user.HasIndex(u => u.NormalizedUserName).IsUnique();
        user.Property(u => u.Email).IsRequired();
        user.Property(u => u.NormalizedEmail).IsRequired();
        user.HasIndex(u => u.NormalizedEmail).IsUnique();
        user.Property(u => u.DisplayName);
        user.Property(u => u.SecurityStamp);
        user.Property(u => u.CreatedAt);
        user.Property(u => u.PictureVersion);
        user.Property(u => u.HasPicture);

        user.HasOne(u => u.Password)
            .WithOne()
            .HasForeignKey<PasswordCredential>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        user.HasOne(u => u.Picture)
            .WithOne()
            .HasForeignKey<ProfilePicture>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PasswordCredentialConfiguration : IEntityTypeConfiguration<PasswordCredential>
{
    public void Configure(EntityTypeBuilder<PasswordCredential> credential)
    {
        credential.ToTable("PasswordCredentials");
        credential.HasKey(c => c.UserId);
        credential.Property(c => c.Hash).IsRequired();
    }
}

internal sealed class ProfilePictureConfiguration : IEntityTypeConfiguration<ProfilePicture>
{
    public void Configure(EntityTypeBuilder<ProfilePicture> picture)
    {
        picture.ToTable("ProfilePictures");
        picture.HasKey(p => p.UserId);
        picture.Property(p => p.ContentType).IsRequired();
        picture.Property(p => p.Data).IsRequired();
    }
}

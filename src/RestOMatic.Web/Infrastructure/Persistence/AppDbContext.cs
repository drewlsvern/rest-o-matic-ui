using Microsoft.EntityFrameworkCore;

namespace RestOMatic.Web.Infrastructure.Persistence;

/// <summary>
/// The app's one database. It declares no entities itself: each slice keeps
/// its entity mappings (<see cref="IEntityTypeConfiguration{TEntity}"/>) in
/// its own folder and they are found here by scanning the assembly.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

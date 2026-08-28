using CTMS.Application.Common;
using CTMS.Domain.Common;
using CTMS.Domain.Locales;
using CTMS.Domain.Projects;
using CTMS.Domain.Translations;
using Microsoft.EntityFrameworkCore;

namespace CTMS.Infrastructure.Persistence;

public sealed class CtmsDbContext : DbContext, IUnitOfWork
{
    public CtmsDbContext(DbContextOptions<CtmsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Locale> Locales => Set<Locale>();

    public DbSet<TranslationKey> TranslationKeys => Set<TranslationKey>();

    public DbSet<TranslationString> TranslationStrings => Set<TranslationString>();

    public override int SaveChanges()
    {
        StampTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CtmsDbContext).Assembly);

        // TranslationString's optimistic-concurrency token maps to PostgreSQL's xmin
        // system column. Other providers (SQLite in the test suite) fall back to a plain
        // concurrency token on the CLR property.
        var version = modelBuilder.Entity<TranslationString>().Property(s => s.Version);
        if (Database.IsNpgsql())
        {
            version.IsRowVersion();
        }
        else
        {
            version.IsConcurrencyToken();
        }
    }

    private void StampTimestamps()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;
                default:
                    break;
            }
        }
    }
}

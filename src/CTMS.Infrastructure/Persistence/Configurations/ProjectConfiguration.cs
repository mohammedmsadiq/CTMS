using CTMS.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CTMS.Infrastructure.Persistence.Configurations;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Slug).IsRequired().HasMaxLength(120);
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.BaseLocaleCode).IsRequired().HasMaxLength(35);

        builder.HasIndex(p => p.Slug).IsUnique();
    }
}

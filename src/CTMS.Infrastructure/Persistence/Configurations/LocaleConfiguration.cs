using CTMS.Domain.Locales;
using CTMS.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CTMS.Infrastructure.Persistence.Configurations;

internal sealed class LocaleConfiguration : IEntityTypeConfiguration<Locale>
{
    public void Configure(EntityTypeBuilder<Locale> builder)
    {
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Code).IsRequired().HasMaxLength(35);
        builder.Property(l => l.DisplayName).IsRequired().HasMaxLength(120);

        builder.HasIndex(l => new { l.ProjectId, l.Code }).IsUnique();

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(l => l.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using CTMS.Domain.Projects;
using CTMS.Domain.Translations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CTMS.Infrastructure.Persistence.Configurations;

internal sealed class TranslationKeyConfiguration : IEntityTypeConfiguration<TranslationKey>
{
    public void Configure(EntityTypeBuilder<TranslationKey> builder)
    {
        builder.HasKey(k => k.Id);

        builder.Property(k => k.KeyName).IsRequired().HasMaxLength(512);
        builder.Property(k => k.Description).HasMaxLength(2000);

        builder.HasIndex(k => new { k.ProjectId, k.KeyName }).IsUnique();

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(k => k.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using CTMS.Domain.Locales;
using CTMS.Domain.Translations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CTMS.Infrastructure.Persistence.Configurations;

internal sealed class TranslationStringConfiguration : IEntityTypeConfiguration<TranslationString>
{
    public void Configure(EntityTypeBuilder<TranslationString> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Value).IsRequired();
        builder.Property(s => s.UpdatedBy).IsRequired().HasMaxLength(256);
        builder.Property(s => s.ReviewState)
            .IsRequired()
            .HasMaxLength(32)
            .HasConversion<string>();

        builder.HasIndex(s => new { s.TranslationKeyId, s.LocaleId }).IsUnique();

        builder.HasOne<TranslationKey>()
            .WithMany()
            .HasForeignKey(s => s.TranslationKeyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Locale>()
            .WithMany()
            .HasForeignKey(s => s.LocaleId)
            .OnDelete(DeleteBehavior.Cascade);

        // The concurrency token on s.Version is finalized in CtmsDbContext.OnModelCreating
        // (xmin for PostgreSQL, a plain token elsewhere).
    }
}

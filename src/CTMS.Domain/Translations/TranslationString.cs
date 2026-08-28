using CTMS.Domain.Common;

namespace CTMS.Domain.Translations;

/// <summary>The value of a <see cref="TranslationKey"/> in one locale.</summary>
public sealed class TranslationString : Entity
{
    private TranslationString()
    {
        // EF Core materialization constructor.
    }

    public TranslationString(Guid translationKeyId, Guid localeId, string value, string updatedBy)
    {
        if (translationKeyId == Guid.Empty)
        {
            throw new ArgumentException("A translation string must reference a key.", nameof(translationKeyId));
        }

        if (localeId == Guid.Empty)
        {
            throw new ArgumentException("A translation string must reference a locale.", nameof(localeId));
        }

        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(updatedBy);

        TranslationKeyId = translationKeyId;
        LocaleId = localeId;
        Value = value;
        UpdatedBy = updatedBy.Trim();
        ReviewState = ReviewState.Draft;
    }

    public Guid TranslationKeyId { get; private set; }

    public Guid LocaleId { get; private set; }

    public string Value { get; private set; } = string.Empty;

    public ReviewState ReviewState { get; private set; }

    public string UpdatedBy { get; private set; } = string.Empty;

    /// <summary>
    /// Optimistic-concurrency token. Mapped to PostgreSQL's <c>xmin</c> system column so
    /// that concurrent edits to the same string are detected when saving.
    /// </summary>
    public uint Version { get; private set; }

    public void Edit(string value, string editedBy)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(editedBy);

        Value = value;
        UpdatedBy = editedBy.Trim();
        ReviewState = ReviewState.NeedsReview;
    }

    public void Approve(string reviewedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewedBy);

        UpdatedBy = reviewedBy.Trim();
        ReviewState = ReviewState.Approved;
    }
}

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

        // Editing published content sends it back for review; a draft stays a draft.
        if (ReviewState != ReviewState.Draft)
        {
            ReviewState = ReviewState.NeedsReview;
        }
    }

    /// <summary>
    /// Applies a review-workflow transition. Legal moves are
    /// Draft→NeedsReview (submit), NeedsReview→Approved (approve),
    /// NeedsReview→Draft (reject) and Approved→NeedsReview (reopen); anything else throws
    /// <see cref="InvalidReviewTransitionException"/>.
    /// </summary>
    public void ChangeReviewState(ReviewState target, string reviewedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewedBy);

        var legal = (ReviewState, target) switch
        {
            (ReviewState.Draft, ReviewState.NeedsReview) => true,
            (ReviewState.NeedsReview, ReviewState.Approved) => true,
            (ReviewState.NeedsReview, ReviewState.Draft) => true,
            (ReviewState.Approved, ReviewState.NeedsReview) => true,
            _ => false,
        };

        if (!legal)
        {
            throw new InvalidReviewTransitionException(ReviewState, target);
        }

        ReviewState = target;
        UpdatedBy = reviewedBy.Trim();
    }
}

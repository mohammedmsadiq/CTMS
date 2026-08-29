using CTMS.Domain.Common;

namespace CTMS.Domain.Translations;

/// <summary>The value of a <see cref="TranslationKey"/> in one language. Last write wins.</summary>
public sealed class TranslationString : Entity
{
    private TranslationString()
    {
        // Materialization constructor for the persistence layer.
    }

    public TranslationString(Guid translationKeyId, string languageCode, string value, string updatedBy)
    {
        if (translationKeyId == Guid.Empty)
        {
            throw new ArgumentException("A translation string must reference a key.", nameof(translationKeyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(languageCode);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(updatedBy);

        TranslationKeyId = translationKeyId;
        LanguageCode = languageCode.Trim();
        Value = value;
        UpdatedBy = updatedBy.Trim();
        ReviewState = ReviewState.Draft;
    }

    public Guid TranslationKeyId { get; private set; }

    /// <summary>BCP-47 code of the language this value is for.</summary>
    public string LanguageCode { get; private set; } = string.Empty;

    public string Value { get; private set; } = string.Empty;

    public ReviewState ReviewState { get; private set; }

    public string UpdatedBy { get; private set; } = string.Empty;

    public void Edit(string value, string editedBy)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(editedBy);

        Value = value;
        UpdatedBy = editedBy.Trim();

        // Editing content that has left Draft (NeedsReview, Approved or Published) sends it
        // back for review; a draft stays a draft.
        if (ReviewState != ReviewState.Draft)
        {
            ReviewState = ReviewState.NeedsReview;
        }
    }

    /// <summary>
    /// Applies a review-workflow transition. Legal moves are
    /// Draft→NeedsReview (submit), NeedsReview→Approved (approve),
    /// NeedsReview→Draft (reject), Approved→NeedsReview (reopen),
    /// Approved→Published (publish) and Published→NeedsReview (reopen); anything else throws
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
            (ReviewState.Approved, ReviewState.Published) => true,
            (ReviewState.Published, ReviewState.NeedsReview) => true,
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

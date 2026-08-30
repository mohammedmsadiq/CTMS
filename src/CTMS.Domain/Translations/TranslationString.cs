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

        // Editing content that has left Draft (InReview, Approved or Published) sends it
        // back for review; a draft stays a draft. An archived string stays archived.
        if (ReviewState is not ReviewState.Draft and not ReviewState.Archived)
        {
            ReviewState = ReviewState.InReview;
        }
    }

    /// <summary>
    /// Applies a review-workflow transition. Legal moves are
    /// Draft→InReview (submit), InReview→Approved (approve),
    /// InReview→Draft (reject), {Approved,Published}→InReview (reopen),
    /// Approved→Published (publish), {Draft,InReview,Approved,Published}→Archived (archive)
    /// and Archived→Draft (unarchive); anything else throws
    /// <see cref="InvalidReviewTransitionException"/>.
    /// </summary>
    public void ChangeReviewState(ReviewState target, string reviewedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewedBy);

        var legal = (ReviewState, target) switch
        {
            (ReviewState.Draft, ReviewState.InReview) => true,
            (ReviewState.InReview, ReviewState.Approved) => true,
            (ReviewState.InReview, ReviewState.Draft) => true,
            (ReviewState.Approved, ReviewState.InReview) => true,
            (ReviewState.Approved, ReviewState.Published) => true,
            (ReviewState.Published, ReviewState.InReview) => true,
            (ReviewState.Draft, ReviewState.Archived) => true,
            (ReviewState.InReview, ReviewState.Archived) => true,
            (ReviewState.Approved, ReviewState.Archived) => true,
            (ReviewState.Published, ReviewState.Archived) => true,
            (ReviewState.Archived, ReviewState.Draft) => true,
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

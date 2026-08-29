namespace CTMS.Domain.Translations;

/// <summary>Lifecycle state of a <see cref="TranslationString"/> in the review workflow.</summary>
public enum ReviewState
{
    Draft = 0,
    NeedsReview = 1,
    Approved = 2,

    /// <summary>An approved string that has been released into a published bundle.</summary>
    Published = 3,
}

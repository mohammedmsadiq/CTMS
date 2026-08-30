namespace CTMS.Domain.Translations;

/// <summary>Lifecycle state of a <see cref="TranslationString"/> in the review workflow.</summary>
public enum ReviewState
{
    Draft = 0,
    InReview = 1,
    Approved = 2,

    /// <summary>An approved string that has been released into a published bundle.</summary>
    Published = 3,

    /// <summary>Retired from the workflow. Never served to consumers and excluded from coverage.</summary>
    Archived = 4,
}

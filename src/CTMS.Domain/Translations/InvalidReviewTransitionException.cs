namespace CTMS.Domain.Translations;

/// <summary>Raised when a review-workflow transition is not one the workflow permits.</summary>
public sealed class InvalidReviewTransitionException : Exception
{
    public InvalidReviewTransitionException(ReviewState from, ReviewState to)
        : base($"A translation string cannot move from '{from}' to '{to}'.")
    {
        From = from;
        To = to;
    }

    public ReviewState From { get; }

    public ReviewState To { get; }
}

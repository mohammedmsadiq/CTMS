using CTMS.Domain.Translations;

namespace CTMS.Application.Tests;

public sealed class ReviewWorkflowTests
{
    [Theory]
    [InlineData(ReviewState.Draft, ReviewState.NeedsReview)]      // submit
    [InlineData(ReviewState.NeedsReview, ReviewState.Approved)]   // approve
    [InlineData(ReviewState.NeedsReview, ReviewState.Draft)]      // reject
    [InlineData(ReviewState.Approved, ReviewState.NeedsReview)]   // reopen
    public void ChangeReviewState_allows_every_legal_transition(ReviewState from, ReviewState to)
    {
        var translationString = StringInState(from);

        translationString.ChangeReviewState(to, "reviewer");

        Assert.Equal(to, translationString.ReviewState);
        Assert.Equal("reviewer", translationString.UpdatedBy);
    }

    [Theory]
    [InlineData(ReviewState.Draft, ReviewState.Approved)]
    [InlineData(ReviewState.Approved, ReviewState.Draft)]
    [InlineData(ReviewState.Approved, ReviewState.Approved)]
    [InlineData(ReviewState.Draft, ReviewState.Draft)]
    public void ChangeReviewState_rejects_illegal_transitions(ReviewState from, ReviewState to)
    {
        var translationString = StringInState(from);

        var exception = Assert.Throws<InvalidReviewTransitionException>(
            () => translationString.ChangeReviewState(to, "reviewer"));

        Assert.Equal(from, exception.From);
        Assert.Equal(to, exception.To);
        Assert.Equal(from, translationString.ReviewState);
    }

    private static TranslationString StringInState(ReviewState state)
    {
        var translationString = new TranslationString(Guid.NewGuid(), Guid.NewGuid(), "value", "author");

        switch (state)
        {
            case ReviewState.Draft:
                break;
            case ReviewState.NeedsReview:
                translationString.ChangeReviewState(ReviewState.NeedsReview, "author");
                break;
            case ReviewState.Approved:
                translationString.ChangeReviewState(ReviewState.NeedsReview, "author");
                translationString.ChangeReviewState(ReviewState.Approved, "author");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }

        return translationString;
    }
}

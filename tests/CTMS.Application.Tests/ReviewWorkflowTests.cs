using CTMS.Domain.Translations;

namespace CTMS.Application.Tests;

public sealed class ReviewWorkflowTests
{
    [Theory]
    [InlineData(ReviewState.Draft, ReviewState.InReview)]          // submit
    [InlineData(ReviewState.InReview, ReviewState.Approved)]       // approve
    [InlineData(ReviewState.InReview, ReviewState.Draft)]          // reject
    [InlineData(ReviewState.Approved, ReviewState.InReview)]       // reopen
    [InlineData(ReviewState.Approved, ReviewState.Published)]      // publish
    [InlineData(ReviewState.Published, ReviewState.InReview)]      // reopen (from published)
    [InlineData(ReviewState.Draft, ReviewState.Archived)]         // archive
    [InlineData(ReviewState.InReview, ReviewState.Archived)]      // archive
    [InlineData(ReviewState.Approved, ReviewState.Archived)]      // archive
    [InlineData(ReviewState.Published, ReviewState.Archived)]     // archive
    [InlineData(ReviewState.Archived, ReviewState.Draft)]         // unarchive
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
    [InlineData(ReviewState.Draft, ReviewState.Published)]
    [InlineData(ReviewState.InReview, ReviewState.Published)]
    [InlineData(ReviewState.Published, ReviewState.Approved)]
    [InlineData(ReviewState.Published, ReviewState.Draft)]
    [InlineData(ReviewState.Published, ReviewState.Published)]
    [InlineData(ReviewState.Archived, ReviewState.InReview)]
    [InlineData(ReviewState.Archived, ReviewState.Approved)]
    [InlineData(ReviewState.Archived, ReviewState.Published)]
    [InlineData(ReviewState.Archived, ReviewState.Archived)]
    public void ChangeReviewState_rejects_illegal_transitions(ReviewState from, ReviewState to)
    {
        var translationString = StringInState(from);

        var exception = Assert.Throws<InvalidReviewTransitionException>(
            () => translationString.ChangeReviewState(to, "reviewer"));

        Assert.Equal(from, exception.From);
        Assert.Equal(to, exception.To);
        Assert.Equal(from, translationString.ReviewState);
    }

    [Fact]
    public void Editing_an_archived_string_keeps_it_archived()
    {
        var translationString = StringInState(ReviewState.Archived);

        translationString.Edit("new value", "editor");

        Assert.Equal(ReviewState.Archived, translationString.ReviewState);
    }

    private static TranslationString StringInState(ReviewState state)
    {
        var translationString = new TranslationString(Guid.NewGuid(), "en-GB", "value", "author");

        switch (state)
        {
            case ReviewState.Draft:
                break;
            case ReviewState.InReview:
                translationString.ChangeReviewState(ReviewState.InReview, "author");
                break;
            case ReviewState.Approved:
                translationString.ChangeReviewState(ReviewState.InReview, "author");
                translationString.ChangeReviewState(ReviewState.Approved, "author");
                break;
            case ReviewState.Published:
                translationString.ChangeReviewState(ReviewState.InReview, "author");
                translationString.ChangeReviewState(ReviewState.Approved, "author");
                translationString.ChangeReviewState(ReviewState.Published, "author");
                break;
            case ReviewState.Archived:
                translationString.ChangeReviewState(ReviewState.Archived, "author");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }

        return translationString;
    }
}

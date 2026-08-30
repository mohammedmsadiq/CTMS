using CTMS.Api.Auth;
using CTMS.Application.Translations;

namespace CTMS.Api.Endpoints;

internal static class ReviewEndpoints
{
    public static IEndpointRouteBuilder MapReviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // All review transitions — submit/approve/reject/reopen and the `publish` action —
        // require CanReview (admin/manager/reviewer). Bulk publish is separate (CanPublish).
        var group = endpoints
            .MapGroup("/api/projects/{project}/keys/{keyId:guid}/strings/{language}/review")
            .WithTags("Review")
            .RequireAuthorization(AuthorizationPolicies.CanReview);

        group.MapPost("/", async (
                string project,
                Guid keyId,
                string language,
                ReviewRequest request,
                TranslationStringService strings,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                // A real bearer token wins: the reviewer is the token identity, not the body field.
                var reviewedBy = TokenActor.Resolve(http.User, request.ReviewedBy, request.ReviewedBy);

                var reviewed = await strings.ReviewAsync(
                    project,
                    keyId,
                    language,
                    request.Action,
                    reviewedBy,
                    cancellationToken);

                return reviewed is null ? Results.NotFound() : Results.Ok(reviewed);
            })
            .WithName("ReviewTranslationString")
            .Produces<TranslationStringDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }
}

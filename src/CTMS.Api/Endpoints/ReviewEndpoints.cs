using CTMS.Api.Auth;
using CTMS.Application.Translations;
using Microsoft.AspNetCore.Authorization;

namespace CTMS.Api.Endpoints;

internal static class ReviewEndpoints
{
    public static IEndpointRouteBuilder MapReviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // A translator may `submit` their own work (CanEditStrings). Every other transition —
        // approve/reject/reopen/publish/archive/unarchive — needs CanReview (admin/manager/
        // reviewer), enforced per-request below. Bulk publish is separate (CanPublish).
        var group = endpoints
            .MapGroup("/api/projects/{project}/keys/{keyId:guid}/strings/{language}/review")
            .WithTags("Review")
            .RequireAuthorization(AuthorizationPolicies.CanEditStrings);

        group.MapPost("/", async (
                string project,
                Guid keyId,
                string language,
                ReviewRequest request,
                TranslationStringService strings,
                IAuthorizationService authorization,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (!ReviewActionRules.IsSubmit(request.Action)
                    && !(await authorization.AuthorizeAsync(http.User, AuthorizationPolicies.CanReview)).Succeeded)
                {
                    return Results.Forbid();
                }

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

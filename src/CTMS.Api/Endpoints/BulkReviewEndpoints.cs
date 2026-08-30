using CTMS.Api.Auth;
using CTMS.Application.Translations;
using Microsoft.AspNetCore.Authorization;

namespace CTMS.Api.Endpoints;

internal static class BulkReviewEndpoints
{
    public static IEndpointRouteBuilder MapBulkReviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Apply one review action across many strings at once. A translator may bulk-`submit`
        // (CanEditStrings); every other action needs CanReview, enforced per-request.
        endpoints.MapPost("/api/projects/{project}/review-bulk", async (
                string project,
                ReviewBulkRequest request,
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

                var reviewedBy = TokenActor.Resolve(http.User, request.ReviewedBy, request.ReviewedBy ?? "system");
                var result = await strings.ReviewBulkAsync(project, request, reviewedBy, cancellationToken);
                return Results.Ok(result);
            })
            .WithName("ReviewTranslationStringsBulk")
            .WithTags("Review")
            .Produces<ReviewBulkResult>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanEditStrings);

        return endpoints;
    }
}

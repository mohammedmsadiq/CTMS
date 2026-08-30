using CTMS.Api.Auth;
using CTMS.Application.Translations;

namespace CTMS.Api.Endpoints;

internal static class BulkReviewEndpoints
{
    public static IEndpointRouteBuilder MapBulkReviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Apply one review action across many strings at once. CanReview (admin/manager/reviewer).
        endpoints.MapPost("/api/applications/{application}/review-bulk", async (
                string application,
                ReviewBulkRequest request,
                TranslationStringService strings,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var reviewedBy = TokenActor.Resolve(http.User, request.ReviewedBy, request.ReviewedBy ?? "system");
                var result = await strings.ReviewBulkAsync(application, request, reviewedBy, cancellationToken);
                return Results.Ok(result);
            })
            .WithName("ReviewTranslationStringsBulk")
            .WithTags("Review")
            .Produces<ReviewBulkResult>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanReview);

        return endpoints;
    }
}

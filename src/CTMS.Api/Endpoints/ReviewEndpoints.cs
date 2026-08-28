using CTMS.Application.Translations;

namespace CTMS.Api.Endpoints;

internal static class ReviewEndpoints
{
    public static IEndpointRouteBuilder MapReviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TODO: auth — require an authenticated principal on this group once auth exists
        // (e.g. group.RequireAuthorization()).
        var group = endpoints
            .MapGroup("/api/projects/{projectId:guid}/keys/{keyId:guid}/strings/{localeId:guid}/review")
            .WithTags("Review");

        group.MapPost("/", async (
                Guid projectId,
                Guid keyId,
                Guid localeId,
                ReviewRequest request,
                TranslationStringService strings,
                CancellationToken cancellationToken) =>
            {
                var reviewed = await strings.ReviewAsync(
                    projectId,
                    keyId,
                    localeId,
                    request.Action,
                    request.ReviewedBy,
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

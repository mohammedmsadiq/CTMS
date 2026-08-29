using CTMS.Api.Auth;
using CTMS.Application.Common;
using CTMS.Application.Translations;

namespace CTMS.Api.Endpoints;

internal static class TranslationStringEndpoints
{
    public static IEndpointRouteBuilder MapTranslationStringEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Reads: any recognised role (CanRead). Upsert: translator and up (CanEditStrings).
        var group = endpoints
            .MapGroup("/api/applications/{application}/keys/{keyId:guid}/strings")
            .WithTags("Translation strings");

        var applicationGroup = endpoints
            .MapGroup("/api/applications/{application}/strings")
            .WithTags("Translation strings")
            .RequireAuthorization(AuthorizationPolicies.CanRead);

        applicationGroup.MapGet("/", async (
                string application,
                TranslationStringService strings,
                CancellationToken cancellationToken,
                string? reviewState = null,
                int skip = 0,
                int take = 50) =>
            {
                var page = await strings.ListByProjectAsync(application, reviewState, skip, take, cancellationToken);
                return page is null ? Results.NotFound() : Results.Ok(page);
            })
            .WithName("ListApplicationTranslationStrings")
            .Produces<PagedResult<TranslationStringDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/", async (
                string application,
                Guid keyId,
                TranslationStringService strings,
                CancellationToken cancellationToken) =>
            {
                var items = await strings.ListByKeyAsync(application, keyId, cancellationToken);
                return items is null ? Results.NotFound() : Results.Ok(items);
            })
            .WithName("ListTranslationStrings")
            .Produces<IReadOnlyList<TranslationStringDto>>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanRead);

        group.MapGet("/{language}", async (
                string application,
                Guid keyId,
                string language,
                TranslationStringService strings,
                CancellationToken cancellationToken) =>
            {
                var translationString = await strings.GetAsync(application, keyId, language, cancellationToken);
                return translationString is null ? Results.NotFound() : Results.Ok(translationString);
            })
            .WithName("GetTranslationString")
            .Produces<TranslationStringDto>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanRead);

        group.MapPut("/{language}", async (
                string application,
                Guid keyId,
                string language,
                UpsertTranslationStringRequest request,
                TranslationStringService strings,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                // A real bearer token wins: the actor is the token identity, the body field is ignored.
                var effective = request with
                {
                    UpdatedBy = TokenActor.Resolve(http.User, request.UpdatedBy, request.UpdatedBy ?? string.Empty),
                };
                var result = await strings.UpsertAsync(application, keyId, language, effective, cancellationToken);
                return result.Created
                    ? Results.CreatedAtRoute("GetTranslationString", new { application, keyId, language }, result.String)
                    : Results.Ok(result.String);
            })
            .WithName("UpsertTranslationString")
            .Produces<TranslationStringDto>(StatusCodes.Status201Created)
            .Produces<TranslationStringDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanEditStrings);

        return endpoints;
    }
}

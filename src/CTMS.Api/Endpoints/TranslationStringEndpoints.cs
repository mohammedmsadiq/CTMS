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
            .MapGroup("/api/projects/{projectId:guid}/keys/{keyId:guid}/strings")
            .WithTags("Translation strings");

        var projectGroup = endpoints
            .MapGroup("/api/projects/{projectId:guid}/strings")
            .WithTags("Translation strings")
            .RequireAuthorization(AuthorizationPolicies.CanRead);

        projectGroup.MapGet("/", async (
                Guid projectId,
                TranslationStringService strings,
                CancellationToken cancellationToken,
                string? reviewState = null,
                int skip = 0,
                int take = 50) =>
            {
                var page = await strings.ListByProjectAsync(projectId, reviewState, skip, take, cancellationToken);
                return page is null ? Results.NotFound() : Results.Ok(page);
            })
            .WithName("ListProjectTranslationStrings")
            .Produces<PagedResult<TranslationStringDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/", async (
                Guid projectId,
                Guid keyId,
                TranslationStringService strings,
                CancellationToken cancellationToken) =>
            {
                var items = await strings.ListByKeyAsync(projectId, keyId, cancellationToken);
                return items is null ? Results.NotFound() : Results.Ok(items);
            })
            .WithName("ListTranslationStrings")
            .Produces<IReadOnlyList<TranslationStringDto>>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanRead);

        group.MapGet("/{localeId:guid}", async (
                Guid projectId,
                Guid keyId,
                Guid localeId,
                TranslationStringService strings,
                CancellationToken cancellationToken) =>
            {
                var translationString = await strings.GetAsync(projectId, keyId, localeId, cancellationToken);
                return translationString is null ? Results.NotFound() : Results.Ok(translationString);
            })
            .WithName("GetTranslationString")
            .Produces<TranslationStringDto>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanRead);

        group.MapPut("/{localeId:guid}", async (
                Guid projectId,
                Guid keyId,
                Guid localeId,
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
                var result = await strings.UpsertAsync(projectId, keyId, localeId, effective, cancellationToken);
                return result.Created
                    ? Results.CreatedAtRoute("GetTranslationString", new { projectId, keyId, localeId }, result.String)
                    : Results.Ok(result.String);
            })
            .WithName("UpsertTranslationString")
            .Produces<TranslationStringDto>(StatusCodes.Status201Created)
            .Produces<TranslationStringDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.CanEditStrings);

        return endpoints;
    }
}

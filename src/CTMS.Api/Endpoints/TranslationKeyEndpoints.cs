using CTMS.Api.Auth;
using CTMS.Application.Common;
using CTMS.Application.Translations;

namespace CTMS.Api.Endpoints;

internal static class TranslationKeyEndpoints
{
    public static IEndpointRouteBuilder MapTranslationKeyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Reads: any recognised role (CanRead). Mutations: admin/manager (CanManageContent).
        var group = endpoints.MapGroup("/api/projects/{project}/keys").WithTags("Translation keys");

        group.MapGet("/", async (
                string project,
                TranslationKeyService keys,
                CancellationToken cancellationToken,
                string? category = null,
                int skip = 0,
                int take = 50) =>
            {
                var page = await keys.ListAsync(project, category, skip, take, cancellationToken);
                return page is null ? Results.NotFound() : Results.Ok(page);
            })
            .WithName("ListTranslationKeys")
            .Produces<PagedResult<TranslationKeyDto>>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanRead);

        group.MapGet("/{keyId:guid}", async (
                string project,
                Guid keyId,
                TranslationKeyService keys,
                CancellationToken cancellationToken) =>
            {
                var key = await keys.GetAsync(project, keyId, cancellationToken);
                return key is null ? Results.NotFound() : Results.Ok(key);
            })
            .WithName("GetTranslationKey")
            .Produces<TranslationKeyDto>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanRead);

        group.MapPost("/", async (
                string project,
                CreateTranslationKeyRequest request,
                TranslationKeyService keys,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var actor = TokenActor.Resolve(http.User, request.CreatedBy, request.CreatedBy ?? "system");
                var created = await keys.CreateAsync(project, request, actor, cancellationToken);
                return Results.CreatedAtRoute("GetTranslationKey", new { project, keyId = created.Id }, created);
            })
            .WithName("CreateTranslationKey")
            .Produces<TranslationKeyDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.CanManageContent);

        group.MapPatch("/{keyId:guid}", async (
                string project,
                Guid keyId,
                UpdateTranslationKeyRequest request,
                TranslationKeyService keys,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var actor = TokenActor.Resolve(http.User, null, "system");
                var updated = await keys.UpdateAsync(project, keyId, request, actor, cancellationToken);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            })
            .WithName("UpdateTranslationKey")
            .Produces<TranslationKeyDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanManageContent);

        group.MapDelete("/{keyId:guid}", async (
                string project,
                Guid keyId,
                TranslationKeyService keys,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var actor = TokenActor.Resolve(http.User, null, "system");
                var deleted = await keys.DeleteAsync(project, keyId, actor, cancellationToken);
                return deleted is true ? Results.NoContent() : Results.NotFound();
            })
            .WithName("DeleteTranslationKey")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanManageContent);

        return endpoints;
    }
}

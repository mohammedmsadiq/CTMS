using CTMS.Api.Auth;
using CTMS.Application.Common;
using CTMS.Application.Translations;

namespace CTMS.Api.Endpoints;

internal static class TranslationKeyEndpoints
{
    public static IEndpointRouteBuilder MapTranslationKeyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Reads: any recognised role (CanRead). Mutations: admin/manager (CanManageContent).
        var group = endpoints.MapGroup("/api/projects/{projectId:guid}/keys").WithTags("Translation keys");

        group.MapGet("/", async (
                Guid projectId,
                TranslationKeyService keys,
                CancellationToken cancellationToken,
                int skip = 0,
                int take = 50) =>
                Results.Ok(await keys.ListAsync(projectId, skip, take, cancellationToken)))
            .WithName("ListTranslationKeys")
            .Produces<PagedResult<TranslationKeyDto>>()
            .RequireAuthorization(AuthorizationPolicies.CanRead);

        group.MapGet("/{keyId:guid}", async (
                Guid projectId,
                Guid keyId,
                TranslationKeyService keys,
                CancellationToken cancellationToken) =>
            {
                var key = await keys.GetAsync(projectId, keyId, cancellationToken);
                return key is null ? Results.NotFound() : Results.Ok(key);
            })
            .WithName("GetTranslationKey")
            .Produces<TranslationKeyDto>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanRead);

        group.MapPost("/", async (
                Guid projectId,
                CreateTranslationKeyRequest request,
                TranslationKeyService keys,
                CancellationToken cancellationToken) =>
            {
                var created = await keys.CreateAsync(projectId, request, cancellationToken);
                return Results.CreatedAtRoute("GetTranslationKey", new { projectId, keyId = created.Id }, created);
            })
            .WithName("CreateTranslationKey")
            .Produces<TranslationKeyDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.CanManageContent);

        group.MapPatch("/{keyId:guid}", async (
                Guid projectId,
                Guid keyId,
                UpdateTranslationKeyRequest request,
                TranslationKeyService keys,
                CancellationToken cancellationToken) =>
            {
                var updated = await keys.UpdateAsync(projectId, keyId, request, cancellationToken);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            })
            .WithName("UpdateTranslationKey")
            .Produces<TranslationKeyDto>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanManageContent);

        group.MapDelete("/{keyId:guid}", async (
                Guid projectId,
                Guid keyId,
                TranslationKeyService keys,
                CancellationToken cancellationToken) =>
            {
                var deleted = await keys.DeleteAsync(projectId, keyId, cancellationToken);
                return deleted ? Results.NoContent() : Results.NotFound();
            })
            .WithName("DeleteTranslationKey")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanManageContent);

        return endpoints;
    }
}

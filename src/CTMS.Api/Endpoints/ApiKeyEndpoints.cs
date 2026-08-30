using CTMS.Api.Auth;
using CTMS.Application.ApiKeys;

namespace CTMS.Api.Endpoints;

internal static class ApiKeyEndpoints
{
    private const string SystemActor = "system";

    public static IEndpointRouteBuilder MapApiKeyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Machine API keys are an admin concern — every route requires CanAdminProjects.
        var group = endpoints
            .MapGroup("/api/api-keys")
            .WithTags("API keys")
            .RequireAuthorization(AuthorizationPolicies.CanAdminProjects);

        group.MapPost("/", async (
                CreateApiKeyRequest request,
                ApiKeyService apiKeys,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var createdBy = TokenActor.Resolve(http.User, null, SystemActor);
                var created = await apiKeys.CreateAsync(request, createdBy, cancellationToken);
                return Results.Created((string?)null, created);
            })
            .WithName("CreateApiKey")
            .Produces<CreatedApiKeyDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/", async (ApiKeyService apiKeys, CancellationToken cancellationToken) =>
                Results.Ok(await apiKeys.ListAsync(cancellationToken)))
            .WithName("ListApiKeys")
            .Produces<IReadOnlyList<ApiKeyDto>>();

        group.MapDelete("/{id:guid}", async (
                Guid id,
                ApiKeyService apiKeys,
                CancellationToken cancellationToken) =>
            {
                var deleted = await apiKeys.DeleteAsync(id, cancellationToken);
                return deleted ? Results.NoContent() : Results.NotFound();
            })
            .WithName("DeleteApiKey")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}

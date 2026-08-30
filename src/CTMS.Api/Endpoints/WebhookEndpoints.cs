using CTMS.Api.Auth;
using CTMS.Application.Webhooks;

namespace CTMS.Api.Endpoints;

internal static class WebhookEndpoints
{
    private const string SystemActor = "system";

    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Webhook registrations are an admin concern — every route requires CanAdminProjects.
        var group = endpoints
            .MapGroup("/api/webhooks")
            .WithTags("Webhooks")
            .RequireAuthorization(AuthorizationPolicies.CanAdminProjects);

        group.MapPost("/", async (
                CreateWebhookRequest request,
                WebhookService webhooks,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var createdBy = TokenActor.Resolve(http.User, null, SystemActor);
                var created = await webhooks.CreateAsync(request, createdBy, cancellationToken);
                return Results.Created((string?)null, created);
            })
            .WithName("CreateWebhook")
            .Produces<CreatedWebhookDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/", async (WebhookService webhooks, CancellationToken cancellationToken) =>
                Results.Ok(await webhooks.ListAsync(cancellationToken)))
            .WithName("ListWebhooks")
            .Produces<IReadOnlyList<WebhookDto>>();

        group.MapDelete("/{id:guid}", async (
                Guid id,
                WebhookService webhooks,
                CancellationToken cancellationToken) =>
            {
                var deleted = await webhooks.DeleteAsync(id, cancellationToken);
                return deleted ? Results.NoContent() : Results.NotFound();
            })
            .WithName("DeleteWebhook")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}

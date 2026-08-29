using CTMS.Application.Audit;
using CTMS.Application.Common;
using CTMS.Application.Projects;
using CTMS.Application.Translations;

namespace CTMS.Api.Endpoints;

internal static class AuditEndpoints
{
    private const string TranslationStringEntityType = "TranslationString";

    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TODO: auth — require an authenticated principal on this group once auth exists
        // (e.g. group.RequireAuthorization()).
        var group = endpoints.MapGroup("/api/projects/{projectId:guid}").WithTags("History");

        group.MapGet("/history", async (
                Guid projectId,
                ProjectService projects,
                AuditService audit,
                CancellationToken cancellationToken,
                int skip = 0,
                int take = 50) =>
            {
                if (await projects.GetAsync(projectId, cancellationToken) is null)
                {
                    return Results.NotFound();
                }

                var page = await audit.ListByProjectAsync(projectId, skip, take, cancellationToken);
                return Results.Ok(page);
            })
            .WithName("ListProjectHistory")
            .Produces<PagedResult<AuditEntryDto>>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/keys/{keyId:guid}/strings/{localeId:guid}/history", async (
                Guid projectId,
                Guid keyId,
                Guid localeId,
                TranslationStringService strings,
                AuditService audit,
                CancellationToken cancellationToken) =>
            {
                var translationString = await strings.GetAsync(projectId, keyId, localeId, cancellationToken);
                if (translationString is null)
                {
                    return Results.NotFound();
                }

                var entries = await audit.ListByEntityAsync(
                    TranslationStringEntityType,
                    translationString.Id,
                    cancellationToken);

                return Results.Ok(entries);
            })
            .WithName("ListTranslationStringHistory")
            .Produces<IReadOnlyList<AuditEntryDto>>()
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}

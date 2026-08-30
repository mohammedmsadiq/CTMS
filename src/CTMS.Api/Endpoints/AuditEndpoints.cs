using CTMS.Api.Auth;
using CTMS.Application.Audit;
using CTMS.Application.Common;
using CTMS.Application.Translations;

namespace CTMS.Api.Endpoints;

internal static class AuditEndpoints
{
    private const string TranslationStringEntityType = "TranslationString";

    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Read-only audit trail: any recognised role (CanRead).
        var group = endpoints
            .MapGroup("/api/projects/{project}")
            .WithTags("History")
            .RequireAuthorization(AuthorizationPolicies.CanRead);

        group.MapGet("/history", async (
                string project,
                AuditService audit,
                CancellationToken cancellationToken,
                int skip = 0,
                int take = 50) =>
            {
                var page = await audit.ListByApplicationAsync(project, skip, take, cancellationToken);
                return page is null ? Results.NotFound() : Results.Ok(page);
            })
            .WithName("ListApplicationHistory")
            .Produces<PagedResult<AuditEntryDto>>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/keys/{keyId:guid}/strings/{language}/history", async (
                string project,
                Guid keyId,
                string language,
                TranslationStringService strings,
                AuditService audit,
                CancellationToken cancellationToken) =>
            {
                var translationString = await strings.GetAsync(project, keyId, language, cancellationToken);
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

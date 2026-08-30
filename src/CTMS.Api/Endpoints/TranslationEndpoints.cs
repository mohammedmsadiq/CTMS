using CTMS.Api.Auth;
using CTMS.Api.Infrastructure;
using CTMS.Application.Common;
using CTMS.Application.Translations;

namespace CTMS.Api.Endpoints;

internal static class TranslationEndpoints
{
    private const string SystemActor = "system";

    public static IEndpointRouteBuilder MapTranslationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var publicReads = endpoints.ServiceProvider
            .GetRequiredService<IConfiguration>()
            .PublicBundleReads();

        var group = endpoints.MapGroup("/api/translations").WithTags("Translations");

        // ---- Client delivery: thin adapter over ITranslationService, ETag + If-None-Match ----
        group.MapGet("/{project}/{language}", async (
                string project,
                string language,
                ITranslationService translations,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                TranslationBundle bundle;
                try
                {
                    bundle = await translations.GetTranslationsAsync(project, language, cancellationToken);
                }
                catch (NotFoundException)
                {
                    return Results.NotFound();
                }

                http.Response.Headers.ETag = $"\"{bundle.ETag}\"";
                http.Response.Headers.CacheControl = "no-cache";

                if (ConditionalRequest.IsNotModified(http.Request.Headers.IfNoneMatch, bundle.ETag))
                {
                    return Results.StatusCode(StatusCodes.Status304NotModified);
                }

                return Results.Ok(
                    new PublishedTranslationsResponse(bundle.Project, bundle.Language, bundle.Translations));
            })
            .WithName("GetPublishedTranslations")
            .Produces<PublishedTranslationsResponse>()
            .Produces(StatusCodes.Status304NotModified)
            .Produces(StatusCodes.Status404NotFound)
            .GatePublicRead(publicReads);

        // ---- Management: grid -----------------------------------------------------------
        group.MapGet("/", async (
                PublishedTranslationsService service,
                CancellationToken cancellationToken,
                string? project = null,
                string? category = null,
                string? language = null,
                string? search = null,
                string? status = null,
                int skip = 0,
                int take = 50) =>
            {
                var page = await service.GetGridAsync(
                    project, category, language, search, skip, take, status, cancellationToken);
                return page is null ? Results.NotFound() : Results.Ok(page);
            })
            .WithName("ListTranslationGrid")
            .Produces<PagedResult<TranslationRowDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanRead);

        // ---- Management: publish preview (diff before publishing) -----------------------
        group.MapGet("/publish/preview", async (
                PublishedTranslationsService service,
                CancellationToken cancellationToken,
                string? project = null,
                string? language = null) =>
            {
                var preview = await service.GetPublishPreviewAsync(project, language, cancellationToken);
                return preview is null ? Results.NotFound() : Results.Ok(preview);
            })
            .WithName("PreviewTranslationsPublish")
            .Produces<PublishPreviewResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanRead);

        // ---- Management: missing ------------------------------------------------------
        group.MapGet("/missing", async (
                PublishedTranslationsService service,
                CancellationToken cancellationToken,
                string? project = null,
                string? language = null,
                int skip = 0,
                int take = 50) =>
            {
                var page = await service.GetMissingAsync(project, language, skip, take, cancellationToken);
                return page is null ? Results.NotFound() : Results.Ok(page);
            })
            .WithName("ListMissingTranslations")
            .Produces<PagedResult<MissingTranslationDto>>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanRead);

        // ---- Management: bulk publish ----------------------------------------------
        group.MapPost("/publish", async (
                PublishTranslationsRequest request,
                PublishedTranslationsService service,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var actor = TokenActor.Resolve(http.User, null, SystemActor);
                var result = await service.BulkPublishAsync(request, actor, cancellationToken);
                return Results.Ok(result);
            })
            .WithName("BulkPublishTranslations")
            .Produces<PublishTranslationsResult>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanPublish);

        // ---- Management: categories ----------------------------------------------
        endpoints.MapGet("/api/categories", async (
                PublishedTranslationsService service,
                CancellationToken cancellationToken,
                string? project = null) =>
            {
                var categories = await service.GetCategoriesAsync(project, cancellationToken);
                return categories is null ? Results.NotFound() : Results.Ok(categories);
            })
            .WithName("ListCategories")
            .WithTags("Translations")
            .Produces<IReadOnlyList<string>>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanRead);

        // ---- Management: dashboard ---------------------------------------------
        endpoints.MapGet("/api/dashboard", async (
                PublishedTranslationsService service,
                CancellationToken cancellationToken,
                string? project = null) =>
            {
                var dashboard = await service.GetDashboardAsync(project, cancellationToken);
                return dashboard is null ? Results.NotFound() : Results.Ok(dashboard);
            })
            .WithName("GetDashboard")
            .WithTags("Translations")
            .Produces<DashboardResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanRead);

        return endpoints;
    }
}

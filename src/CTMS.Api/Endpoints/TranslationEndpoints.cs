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

        // ---- Client delivery: assemble-on-demand, ETag + If-None-Match ----------------------
        group.MapGet("/{application}/{language}", async (
                string application,
                string language,
                PublishedTranslationsService service,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var view = await service.GetPublishedAsync(application, language, cancellationToken);
                if (view is null)
                {
                    return Results.NotFound();
                }

                http.Response.Headers.ETag = $"\"{view.Hash}\"";
                http.Response.Headers.CacheControl = "no-cache";

                if (ConditionalRequest.IsNotModified(http.Request.Headers.IfNoneMatch, view.Hash))
                {
                    return Results.StatusCode(StatusCodes.Status304NotModified);
                }

                return Results.Ok(new PublishedTranslationsResponse(view.Application, view.Language, view.Translations));
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
                string? application = null,
                string? category = null,
                string? language = null,
                string? search = null,
                int skip = 0,
                int take = 50) =>
            {
                var page = await service.GetGridAsync(
                    application, category, language, search, skip, take, cancellationToken);
                return page is null ? Results.NotFound() : Results.Ok(page);
            })
            .WithName("ListTranslationGrid")
            .Produces<PagedResult<TranslationRowDto>>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanRead);

        // ---- Management: missing ------------------------------------------------------
        group.MapGet("/missing", async (
                PublishedTranslationsService service,
                CancellationToken cancellationToken,
                string? application = null,
                string? language = null,
                int skip = 0,
                int take = 50) =>
            {
                var page = await service.GetMissingAsync(application, language, skip, take, cancellationToken);
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
                string? application = null) =>
            {
                var categories = await service.GetCategoriesAsync(application, cancellationToken);
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
                string? application = null) =>
            {
                var dashboard = await service.GetDashboardAsync(application, cancellationToken);
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

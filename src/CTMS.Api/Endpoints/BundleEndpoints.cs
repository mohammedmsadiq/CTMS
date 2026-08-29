using CTMS.Api.Infrastructure;
using CTMS.Application.Translations;

namespace CTMS.Api.Endpoints;

internal static class BundleEndpoints
{
    private const string SystemActor = "system";

    public static IEndpointRouteBuilder MapBundleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TODO: auth — require an authenticated principal on this group once auth exists
        // (e.g. group.RequireAuthorization()).
        var group = endpoints
            .MapGroup("/api/projects/{projectId:guid}/bundles")
            .WithTags("Bundles");

        group.MapPost("/{localeCode}", async (
                Guid projectId,
                string localeCode,
                PublishBundleRequest? request,
                TranslationBundleService bundles,
                CancellationToken cancellationToken) =>
            {
                var publishedBy = string.IsNullOrWhiteSpace(request?.PublishedBy) ? SystemActor : request!.PublishedBy!;
                var bundle = await bundles.PublishAsync(projectId, localeCode, publishedBy, cancellationToken);
                return Results.CreatedAtRoute(
                    "GetBundleByVersion",
                    new { projectId, localeCode = bundle.LocaleCode, version = bundle.Version },
                    bundle);
            })
            .WithName("PublishBundle")
            .Produces<TranslationBundleDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Conditional GET: the latest bundle is fronted by Redis (in-memory fallback locally) and
        // carries a strong ETag. `Cache-Control: no-cache` lets a client store the response but
        // forces revalidation, which an `If-None-Match` turns into a cheap `304` (served straight
        // from the cache, no MongoDB round-trip). `.../versions` and `.../versions/{n}` stay
        // uncached and unconditioned.
        group.MapGet("/{localeCode}", async (
                Guid projectId,
                string localeCode,
                TranslationBundleService bundles,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var bundle = await bundles.GetLatestAsync(projectId, localeCode, cancellationToken);
                if (bundle is null)
                {
                    return Results.NotFound();
                }

                http.Response.Headers.ETag = $"\"{bundle.ETag}\"";
                http.Response.Headers.CacheControl = "no-cache";

                return BundleConditionalRequest.IsNotModified(http.Request.Headers.IfNoneMatch, bundle.ETag)
                    ? Results.StatusCode(StatusCodes.Status304NotModified)
                    : Results.Ok(bundle);
            })
            .WithName("GetLatestBundle")
            .Produces<TranslationBundleDto>()
            .Produces(StatusCodes.Status304NotModified)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{localeCode}/versions", async (
                Guid projectId,
                string localeCode,
                TranslationBundleService bundles,
                CancellationToken cancellationToken) =>
            {
                var versions = await bundles.ListVersionsAsync(projectId, localeCode, cancellationToken);
                return versions is null ? Results.NotFound() : Results.Ok(versions);
            })
            .WithName("ListBundleVersions")
            .Produces<IReadOnlyList<BundleVersionDto>>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{localeCode}/versions/{version:int}", async (
                Guid projectId,
                string localeCode,
                int version,
                TranslationBundleService bundles,
                CancellationToken cancellationToken) =>
            {
                var bundle = await bundles.GetByVersionAsync(projectId, localeCode, version, cancellationToken);
                return bundle is null ? Results.NotFound() : Results.Ok(bundle);
            })
            .WithName("GetBundleByVersion")
            .Produces<TranslationBundleDto>()
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}

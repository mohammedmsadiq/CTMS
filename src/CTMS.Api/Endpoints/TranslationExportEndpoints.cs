using CTMS.Api.Auth;
using CTMS.Application.Translations.Export;

namespace CTMS.Api.Endpoints;

internal static class TranslationExportEndpoints
{
    public static IEndpointRouteBuilder MapTranslationExportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Download a project's translations as a translator work file (CSV or XLSX). Any read role.
        endpoints.MapGet("/api/projects/{project}/export", async (
                string project,
                string format,
                TranslationExportService export,
                CancellationToken cancellationToken,
                string? language = null,
                string? category = null,
                bool includeInactiveKeys = false,
                string? status = null) =>
            {
                var file = await export.ExportAsync(
                    project,
                    new TranslationExportQuery(format, language, category, includeInactiveKeys, status),
                    cancellationToken);

                return file is null
                    ? Results.NotFound()
                    : Results.File(file.Bytes, file.ContentType, file.FileName);
            })
            .WithName("ExportTranslations")
            .WithTags("Translations")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanRead);

        return endpoints;
    }
}

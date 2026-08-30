using CTMS.Api.Auth;
using CTMS.Api.Infrastructure;
using CTMS.Application.Translations.Import;

namespace CTMS.Api.Endpoints;

internal static class TranslationImportEndpoints
{
    public static IEndpointRouteBuilder MapTranslationImportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Bulk import a translation file into one (application, language). Admin/manager only.
        endpoints.MapPost("/api/applications/{application}/import", async (
                string application,
                ImportTranslationsRequest request,
                TranslationImportService import,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var actor = TokenActor.Resolve(http.User, null, "system");
                var result = await import.ImportAsync(application, request, actor, cancellationToken);
                return Results.Ok(result);
            })
            .WithName("ImportTranslations")
            .WithTags("Translations")
            .WithMetadata(new RequestBodySizeLimit.LargeImportBody())
            .Produces<ImportTranslationsResult>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanManageContent);

        return endpoints;
    }
}

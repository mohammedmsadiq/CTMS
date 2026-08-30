using CTMS.Api.Auth;
using CTMS.Application.Languages;

namespace CTMS.Api.Endpoints;

internal static class LanguageEndpoints
{
    public static IEndpointRouteBuilder MapLanguageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var publicReads = endpoints.ServiceProvider
            .GetRequiredService<IConfiguration>()
            .PublicBundleReads();

        var group = endpoints.MapGroup("/api/languages").WithTags("Languages");

        // Client-facing catalogue read: anonymous by default (Auth:PublicBundleReads).
        group.MapGet("/", async (
                LanguageService languages,
                CancellationToken cancellationToken,
                bool includeInactive = false) =>
                Results.Ok(await languages.ListAsync(includeInactive, cancellationToken)))
            .WithName("ListLanguages")
            .Produces<IReadOnlyList<LanguageDto>>()
            .GatePublicRead(publicReads);

        // Static BCP-47 catalogue for the Admin UI wizard. Anonymous by default (Auth:PublicBundleReads).
        group.MapGet("/suggestions", (LanguageService languages) => Results.Ok(languages.Suggestions()))
            .WithName("ListLanguageSuggestions")
            .Produces<IReadOnlyList<LanguageSuggestionDto>>()
            .GatePublicRead(publicReads);

        // Idempotent bulk register: existing codes are skipped, not errored.
        group.MapPost("/bulk", async (
                BulkCreateLanguagesRequest request,
                LanguageService languages,
                CancellationToken cancellationToken) =>
                Results.Ok(await languages.BulkCreateAsync(request, cancellationToken)))
            .WithName("BulkCreateLanguages")
            .Produces<BulkCreateLanguagesResult>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization(AuthorizationPolicies.CanManageContent);

        group.MapGet("/{code}", async (string code, LanguageService languages, CancellationToken cancellationToken) =>
            {
                var language = await languages.GetAsync(code, cancellationToken);
                return language is null ? Results.NotFound() : Results.Ok(language);
            })
            .WithName("GetLanguage")
            .Produces<LanguageDto>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanRead);

        group.MapPost("/", async (
                CreateLanguageRequest request,
                LanguageService languages,
                CancellationToken cancellationToken) =>
            {
                var created = await languages.CreateAsync(request, cancellationToken);
                return Results.CreatedAtRoute("GetLanguage", new { code = created.Code }, created);
            })
            .WithName("CreateLanguage")
            .Produces<LanguageDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.CanManageContent);

        group.MapPatch("/{code}", async (
                string code,
                UpdateLanguageRequest request,
                LanguageService languages,
                CancellationToken cancellationToken) =>
            {
                var updated = await languages.UpdateAsync(code, request, cancellationToken);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            })
            .WithName("UpdateLanguage")
            .Produces<LanguageDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanManageContent);

        return endpoints;
    }
}

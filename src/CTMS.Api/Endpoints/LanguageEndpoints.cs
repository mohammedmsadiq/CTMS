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

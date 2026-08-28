using CTMS.Application.Locales;

namespace CTMS.Api.Endpoints;

internal static class LocaleEndpoints
{
    public static IEndpointRouteBuilder MapLocaleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TODO: auth — require an authenticated principal on this group once auth exists
        // (e.g. group.RequireAuthorization()).
        var group = endpoints.MapGroup("/api/projects/{projectId:guid}/locales").WithTags("Locales");

        group.MapGet("/", async (Guid projectId, LocaleService locales, CancellationToken cancellationToken) =>
                Results.Ok(await locales.ListAsync(projectId, cancellationToken)))
            .WithName("ListLocales")
            .Produces<IReadOnlyList<LocaleDto>>();

        group.MapGet("/{localeId:guid}", async (
                Guid projectId,
                Guid localeId,
                LocaleService locales,
                CancellationToken cancellationToken) =>
            {
                var locale = await locales.GetAsync(projectId, localeId, cancellationToken);
                return locale is null ? Results.NotFound() : Results.Ok(locale);
            })
            .WithName("GetLocale")
            .Produces<LocaleDto>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", async (
                Guid projectId,
                CreateLocaleRequest request,
                LocaleService locales,
                CancellationToken cancellationToken) =>
            {
                var created = await locales.CreateAsync(projectId, request, cancellationToken);
                return Results.CreatedAtRoute("GetLocale", new { projectId, localeId = created.Id }, created);
            })
            .WithName("CreateLocale")
            .Produces<LocaleDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{localeId:guid}", async (
                Guid projectId,
                Guid localeId,
                UpdateLocaleRequest request,
                LocaleService locales,
                CancellationToken cancellationToken) =>
            {
                var updated = await locales.UpdateAsync(projectId, localeId, request, cancellationToken);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            })
            .WithName("UpdateLocale")
            .Produces<LocaleDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{localeId:guid}", async (
                Guid projectId,
                Guid localeId,
                LocaleService locales,
                CancellationToken cancellationToken) =>
            {
                var deleted = await locales.DeleteAsync(projectId, localeId, cancellationToken);
                return deleted ? Results.NoContent() : Results.NotFound();
            })
            .WithName("DeleteLocale")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}

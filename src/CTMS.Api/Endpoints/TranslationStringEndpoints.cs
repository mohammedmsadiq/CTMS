using CTMS.Application.Translations;

namespace CTMS.Api.Endpoints;

internal static class TranslationStringEndpoints
{
    public static IEndpointRouteBuilder MapTranslationStringEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TODO: auth — require an authenticated principal on this group once auth exists
        // (e.g. group.RequireAuthorization()).
        var group = endpoints
            .MapGroup("/api/projects/{projectId:guid}/keys/{keyId:guid}/strings")
            .WithTags("Translation strings");

        group.MapGet("/", async (
                Guid projectId,
                Guid keyId,
                TranslationStringService strings,
                CancellationToken cancellationToken) =>
            {
                var items = await strings.ListByKeyAsync(projectId, keyId, cancellationToken);
                return items is null ? Results.NotFound() : Results.Ok(items);
            })
            .WithName("ListTranslationStrings")
            .Produces<IReadOnlyList<TranslationStringDto>>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{localeId:guid}", async (
                Guid projectId,
                Guid keyId,
                Guid localeId,
                TranslationStringService strings,
                CancellationToken cancellationToken) =>
            {
                var translationString = await strings.GetAsync(projectId, keyId, localeId, cancellationToken);
                return translationString is null ? Results.NotFound() : Results.Ok(translationString);
            })
            .WithName("GetTranslationString")
            .Produces<TranslationStringDto>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/{localeId:guid}", async (
                Guid projectId,
                Guid keyId,
                Guid localeId,
                UpsertTranslationStringRequest request,
                TranslationStringService strings,
                CancellationToken cancellationToken) =>
            {
                var result = await strings.UpsertAsync(projectId, keyId, localeId, request, cancellationToken);
                return result.Created
                    ? Results.CreatedAtRoute("GetTranslationString", new { projectId, keyId, localeId }, result.String)
                    : Results.Ok(result.String);
            })
            .WithName("UpsertTranslationString")
            .Produces<TranslationStringDto>(StatusCodes.Status201Created)
            .Produces<TranslationStringDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }
}

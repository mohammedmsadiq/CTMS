using CTMS.Api.Auth;
using CTMS.Application.Projects;

namespace CTMS.Api.Endpoints;

internal static class ApplicationEndpoints
{
    public static IEndpointRouteBuilder MapApplicationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var publicReads = endpoints.ServiceProvider
            .GetRequiredService<IConfiguration>()
            .PublicBundleReads();

        var group = endpoints.MapGroup("/api/applications").WithTags("Applications");

        // Client-facing catalogue read: anonymous by default (Auth:PublicBundleReads).
        group.MapGet("/", async (
                ProjectService projects,
                CancellationToken cancellationToken,
                bool includeInactive = false) =>
                Results.Ok(await projects.ListAsync(includeInactive, cancellationToken)))
            .WithName("ListApplications")
            .Produces<IReadOnlyList<ApplicationDto>>()
            .GatePublicRead(publicReads);

        group.MapGet("/{code}", async (string code, ProjectService projects, CancellationToken cancellationToken) =>
            {
                var application = await projects.GetAsync(code, cancellationToken);
                return application is null ? Results.NotFound() : Results.Ok(application);
            })
            .WithName("GetApplication")
            .Produces<ApplicationDto>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanRead);

        group.MapPost("/", async (
                CreateApplicationRequest request,
                ProjectService projects,
                CancellationToken cancellationToken) =>
            {
                var created = await projects.CreateAsync(request, cancellationToken);
                return Results.CreatedAtRoute("GetApplication", new { code = created.Code }, created);
            })
            .WithName("CreateApplication")
            .Produces<ApplicationDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.CanAdminProjects);

        group.MapPatch("/{code}", async (
                string code,
                UpdateApplicationRequest request,
                ProjectService projects,
                CancellationToken cancellationToken) =>
            {
                var updated = await projects.UpdateAsync(code, request, cancellationToken);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            })
            .WithName("UpdateApplication")
            .Produces<ApplicationDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanManageContent);

        group.MapPut("/{code}/languages/{language}", async (
                string code,
                string language,
                ProjectService projects,
                CancellationToken cancellationToken) =>
            {
                var updated = await projects.EnableLanguageAsync(code, language, cancellationToken);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            })
            .WithName("EnableApplicationLanguage")
            .Produces<ApplicationDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanManageContent);

        group.MapDelete("/{code}/languages/{language}", async (
                string code,
                string language,
                ProjectService projects,
                CancellationToken cancellationToken) =>
            {
                var updated = await projects.DisableLanguageAsync(code, language, cancellationToken);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            })
            .WithName("DisableApplicationLanguage")
            .Produces<ApplicationDto>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanManageContent);

        return endpoints;
    }
}

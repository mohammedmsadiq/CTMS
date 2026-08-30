using CTMS.Api.Auth;
using CTMS.Application.Projects;

namespace CTMS.Api.Endpoints;

internal static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var publicReads = endpoints.ServiceProvider
            .GetRequiredService<IConfiguration>()
            .PublicBundleReads();

        var group = endpoints.MapGroup("/api/projects").WithTags("Projects");

        // Client-facing catalogue read: anonymous by default (Auth:PublicBundleReads).
        group.MapGet("/", async (
                ProjectService projects,
                CancellationToken cancellationToken,
                bool includeInactive = false) =>
                Results.Ok(await projects.ListAsync(includeInactive, cancellationToken)))
            .WithName("ListProjects")
            .Produces<IReadOnlyList<ProjectDto>>()
            .GatePublicRead(publicReads);

        group.MapGet("/{code}", async (string code, ProjectService projects, CancellationToken cancellationToken) =>
            {
                var project = await projects.GetAsync(code, cancellationToken);
                return project is null ? Results.NotFound() : Results.Ok(project);
            })
            .WithName("GetProject")
            .Produces<ProjectDto>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanRead);

        group.MapPost("/", async (
                CreateProjectRequest request,
                ProjectService projects,
                CancellationToken cancellationToken) =>
            {
                var created = await projects.CreateAsync(request, cancellationToken);
                return Results.CreatedAtRoute("GetProject", new { code = created.Code }, created);
            })
            .WithName("CreateProject")
            .Produces<ProjectDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.CanAdminProjects);

        group.MapPatch("/{code}", async (
                string code,
                UpdateProjectRequest request,
                ProjectService projects,
                CancellationToken cancellationToken) =>
            {
                var updated = await projects.UpdateAsync(code, request, cancellationToken);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            })
            .WithName("UpdateProject")
            .Produces<ProjectDto>()
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
            .WithName("EnableProjectLanguage")
            .Produces<ProjectDto>()
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
            .WithName("DisableProjectLanguage")
            .Produces<ProjectDto>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanManageContent);

        return endpoints;
    }
}

using CTMS.Api.Auth;
using CTMS.Application.Projects;

namespace CTMS.Api.Endpoints;

internal static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Reads: any recognised role. Create: admin only (there is no project delete endpoint).
        var group = endpoints.MapGroup("/api/projects").WithTags("Projects");

        group.MapGet("/", async (ProjectService projects, CancellationToken cancellationToken) =>
                Results.Ok(await projects.ListAsync(cancellationToken)))
            .WithName("ListProjects")
            .Produces<IReadOnlyList<ProjectDto>>()
            .RequireAuthorization(AuthorizationPolicies.CanRead);

        group.MapGet("/{id:guid}", async (Guid id, ProjectService projects, CancellationToken cancellationToken) =>
            {
                var project = await projects.GetAsync(id, cancellationToken);
                return project is null ? Results.NotFound() : Results.Ok(project);
            })
            .WithName("GetProject")
            .Produces<ProjectDto>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.CanRead);

        group.MapPost("/", async (CreateProjectRequest request, ProjectService projects, CancellationToken cancellationToken) =>
            {
                var created = await projects.CreateAsync(request, cancellationToken);
                return Results.CreatedAtRoute("GetProject", new { id = created.Id }, created);
            })
            .WithName("CreateProject")
            .Produces<ProjectDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthorizationPolicies.CanAdminProjects);

        return endpoints;
    }
}

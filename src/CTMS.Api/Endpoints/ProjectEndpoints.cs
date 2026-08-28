using CTMS.Application.Projects;

namespace CTMS.Api.Endpoints;

internal static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TODO: auth — require an authenticated principal on this group once auth exists
        // (e.g. group.RequireAuthorization()).
        var group = endpoints.MapGroup("/api/projects").WithTags("Projects");

        group.MapGet("/", async (ProjectService projects, CancellationToken cancellationToken) =>
                Results.Ok(await projects.ListAsync(cancellationToken)))
            .WithName("ListProjects")
            .Produces<IReadOnlyList<ProjectDto>>();

        group.MapGet("/{id:guid}", async (Guid id, ProjectService projects, CancellationToken cancellationToken) =>
            {
                var project = await projects.GetAsync(id, cancellationToken);
                return project is null ? Results.NotFound() : Results.Ok(project);
            })
            .WithName("GetProject")
            .Produces<ProjectDto>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", async (CreateProjectRequest request, ProjectService projects, CancellationToken cancellationToken) =>
            {
                var created = await projects.CreateAsync(request, cancellationToken);
                return Results.CreatedAtRoute("GetProject", new { id = created.Id }, created);
            })
            .WithName("CreateProject")
            .Produces<ProjectDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }
}

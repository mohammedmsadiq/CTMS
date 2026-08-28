using CTMS.Application.Common;
using CTMS.Domain.Projects;

namespace CTMS.Application.Projects;

/// <summary>Use-case orchestration for projects.</summary>
public sealed class ProjectService
{
    private readonly IProjectRepository _projects;
    private readonly IUnitOfWork _unitOfWork;

    public ProjectService(IProjectRepository projects, IUnitOfWork unitOfWork)
    {
        _projects = projects;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<ProjectDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var projects = await _projects.ListAsync(cancellationToken);
        return projects.Select(ToDto).ToList();
    }

    public async Task<ProjectDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetByIdAsync(id, cancellationToken);
        return project is null ? null : ToDto(project);
    }

    public async Task<ProjectDto> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("A project name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.BaseLocaleCode))
        {
            throw new ValidationException("A base locale code is required.");
        }

        var slug = Slug.From(string.IsNullOrWhiteSpace(request.Slug) ? request.Name : request.Slug);

        if (string.IsNullOrEmpty(slug))
        {
            throw new ValidationException("A project slug could not be derived; provide an explicit slug.");
        }

        if (await _projects.SlugExistsAsync(slug, cancellationToken))
        {
            throw new SlugAlreadyInUseException(slug);
        }

        var project = new Project(request.Name, slug, request.BaseLocaleCode, request.Description);

        await _projects.AddAsync(project, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(project);
    }

    private static ProjectDto ToDto(Project project) => new(
        project.Id,
        project.Name,
        project.Slug,
        project.Description,
        project.BaseLocaleCode,
        project.CreatedAt,
        project.UpdatedAt);
}

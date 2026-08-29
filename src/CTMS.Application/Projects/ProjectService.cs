using CTMS.Application.Common;
using CTMS.Application.Languages;
using CTMS.Domain.Projects;

namespace CTMS.Application.Projects;

/// <summary>Use-case orchestration for applications.</summary>
public sealed class ProjectService
{
    private readonly IProjectRepository _projects;
    private readonly ILanguageRepository _languages;
    private readonly IUnitOfWork _unitOfWork;

    public ProjectService(
        IProjectRepository projects,
        ILanguageRepository languages,
        IUnitOfWork unitOfWork)
    {
        _projects = projects;
        _languages = languages;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<ApplicationDto>> ListAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var projects = await _projects.ListAsync(includeInactive, cancellationToken);
        return projects.Select(ToDto).ToList();
    }

    public async Task<ApplicationDto?> GetAsync(string code, CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetBySlugAsync(Slug.From(code ?? string.Empty), cancellationToken);
        return project is null ? null : ToDto(project);
    }

    public async Task<ApplicationDto> CreateAsync(
        CreateApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("An application name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.BaseLanguageCode))
        {
            throw new ValidationException("A base language code is required.");
        }

        var slug = Slug.From(string.IsNullOrWhiteSpace(request.Code) ? request.Name : request.Code);
        if (string.IsNullOrEmpty(slug))
        {
            throw new ValidationException("An application code could not be derived; provide an explicit code.");
        }

        if (await _projects.SlugExistsAsync(slug, cancellationToken))
        {
            throw new SlugAlreadyInUseException(slug);
        }

        var project = new Project(request.Name, slug, request.BaseLanguageCode, request.Description, request.IsShared);

        if (request.EnabledLanguageCodes is { Count: > 0 } enabled)
        {
            await ValidateLanguagesAsync(enabled, cancellationToken);
            project.SetEnabledLanguages(enabled);
        }

        await _projects.AddAsync(project, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(project);
    }

    public async Task<ApplicationDto?> UpdateAsync(
        string code,
        UpdateApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var project = await _projects.GetBySlugAsync(Slug.From(code ?? string.Empty), cancellationToken);
        if (project is null)
        {
            return null;
        }

        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                throw new ValidationException("An application name cannot be blank.");
            }

            project.Rename(request.Name);
        }

        if (request.Description is not null)
        {
            project.UpdateDescription(request.Description.Length == 0 ? null : request.Description);
        }

        if (request.BaseLanguageCode is not null)
        {
            if (string.IsNullOrWhiteSpace(request.BaseLanguageCode))
            {
                throw new ValidationException("A base language code cannot be blank.");
            }

            project.SetBaseLanguageCode(request.BaseLanguageCode);
        }

        if (request.IsShared is { } isShared)
        {
            project.SetShared(isShared);
        }

        if (request.Active is { } active)
        {
            project.SetActive(active);
        }

        if (request.EnabledLanguageCodes is { } enabled)
        {
            await ValidateLanguagesAsync(enabled, cancellationToken);
            project.SetEnabledLanguages(enabled);
        }

        await _projects.UpdateAsync(project, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(project);
    }

    /// <summary>Adds a language to an application's enabled set. <c>null</c> if the application is unknown.</summary>
    public async Task<ApplicationDto?> EnableLanguageAsync(
        string code,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetBySlugAsync(Slug.From(code ?? string.Empty), cancellationToken);
        if (project is null)
        {
            return null;
        }

        await ValidateLanguagesAsync([languageCode], cancellationToken);
        project.EnableLanguage(languageCode);

        await _projects.UpdateAsync(project, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(project);
    }

    /// <summary>Removes a language from an application's enabled set. <c>null</c> if the application is unknown.</summary>
    public async Task<ApplicationDto?> DisableLanguageAsync(
        string code,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetBySlugAsync(Slug.From(code ?? string.Empty), cancellationToken);
        if (project is null)
        {
            return null;
        }

        project.DisableLanguage(languageCode);

        await _projects.UpdateAsync(project, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(project);
    }

    private async Task ValidateLanguagesAsync(
        IEnumerable<string> codes,
        CancellationToken cancellationToken)
    {
        var known = (await _languages.ListAllAsync(cancellationToken))
            .ToDictionary(l => l.Code, StringComparer.OrdinalIgnoreCase);

        foreach (var raw in codes)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var code = raw.Trim();
            if (!known.TryGetValue(code, out var language))
            {
                throw new ValidationException($"Language '{code}' is not registered.");
            }

            if (!language.Active)
            {
                throw new ValidationException($"Language '{code}' is not active.");
            }
        }
    }

    private static ApplicationDto ToDto(Project project) => new(
        project.Slug,
        project.Name,
        project.Description,
        project.IsShared,
        project.Active,
        project.BaseLanguageCode,
        project.EnabledLanguageCodes,
        project.CreatedAt,
        project.UpdatedAt);
}

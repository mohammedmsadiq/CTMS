using CTMS.Application.Common;
using CTMS.Application.Projects;
using CTMS.Domain.Locales;

namespace CTMS.Application.Locales;

/// <summary>Use-case orchestration for the locales enabled on a project.</summary>
public sealed class LocaleService
{
    private readonly ILocaleRepository _locales;
    private readonly IProjectRepository _projects;
    private readonly IUnitOfWork _unitOfWork;

    public LocaleService(ILocaleRepository locales, IProjectRepository projects, IUnitOfWork unitOfWork)
    {
        _locales = locales;
        _projects = projects;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<LocaleDto>> ListAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var locales = await _locales.ListByProjectAsync(projectId, cancellationToken);
        return locales.Select(ToDto).ToList();
    }

    public async Task<LocaleDto?> GetAsync(Guid projectId, Guid localeId, CancellationToken cancellationToken = default)
    {
        var locale = await _locales.GetAsync(projectId, localeId, cancellationToken);
        return locale is null ? null : ToDto(locale);
    }

    public async Task<LocaleDto> CreateAsync(Guid projectId, CreateLocaleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var code = NormalizeCode(request.Code);

        if (code.Length == 0)
        {
            throw new ValidationException("A locale code is required.");
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            throw new ValidationException("A locale display name is required.");
        }

        if (!await _projects.ExistsAsync(projectId, cancellationToken))
        {
            throw new NotFoundException($"Project '{projectId}' was not found.");
        }

        if (await _locales.CodeExistsAsync(projectId, code, cancellationToken))
        {
            throw new ConflictException($"A locale with the code '{code}' already exists in this project.");
        }

        var locale = new Locale(projectId, code, request.DisplayName, request.IsRtl);

        await _locales.AddAsync(locale, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(locale);
    }

    public async Task<LocaleDto?> UpdateAsync(
        Guid projectId,
        Guid localeId,
        UpdateLocaleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var locale = await _locales.GetAsync(projectId, localeId, cancellationToken);
        if (locale is null)
        {
            return null;
        }

        if (request.DisplayName is not null)
        {
            if (string.IsNullOrWhiteSpace(request.DisplayName))
            {
                throw new ValidationException("A locale display name cannot be blank.");
            }

            locale.SetDisplayName(request.DisplayName);
        }

        if (request.IsRtl is { } isRtl)
        {
            locale.SetRightToLeft(isRtl);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(locale);
    }

    public async Task<bool> DeleteAsync(Guid projectId, Guid localeId, CancellationToken cancellationToken = default)
    {
        var locale = await _locales.GetAsync(projectId, localeId, cancellationToken);
        if (locale is null)
        {
            return false;
        }

        await _locales.RemoveAsync(locale, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>Trims and collapses internal whitespace; BCP-47 casing is left untouched.</summary>
    private static string NormalizeCode(string? code)
        => string.IsNullOrWhiteSpace(code)
            ? string.Empty
            : string.Join(' ', code.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static LocaleDto ToDto(Locale locale) => new(
        locale.Id,
        locale.ProjectId,
        locale.Code,
        locale.DisplayName,
        locale.IsRtl,
        locale.CreatedAt,
        locale.UpdatedAt);
}

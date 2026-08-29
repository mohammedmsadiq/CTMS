using CTMS.Application.Common;
using CTMS.Application.Projects;
using CTMS.Domain.Translations;

namespace CTMS.Application.Translations;

/// <summary>Use-case orchestration for translation keys.</summary>
public sealed class TranslationKeyService
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private readonly ITranslationKeyRepository _keys;
    private readonly IProjectRepository _projects;
    private readonly IUnitOfWork _unitOfWork;

    public TranslationKeyService(
        ITranslationKeyRepository keys,
        IProjectRepository projects,
        IUnitOfWork unitOfWork)
    {
        _keys = keys;
        _projects = projects;
        _unitOfWork = unitOfWork;
    }

    public async Task<PagedResult<TranslationKeyDto>> ListAsync(
        Guid projectId,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0)
        {
            skip = 0;
        }

        take = take switch
        {
            <= 0 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => take,
        };

        var total = await _keys.CountByProjectAsync(projectId, cancellationToken);
        var keys = await _keys.ListByProjectAsync(projectId, skip, take, cancellationToken);

        return new PagedResult<TranslationKeyDto>(keys.Select(ToDto).ToList(), total);
    }

    public async Task<TranslationKeyDto?> GetAsync(Guid projectId, Guid keyId, CancellationToken cancellationToken = default)
    {
        var key = await _keys.GetAsync(projectId, keyId, cancellationToken);
        return key is null ? null : ToDto(key);
    }

    public async Task<TranslationKeyDto> CreateAsync(
        Guid projectId,
        CreateTranslationKeyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var keyName = request.KeyName?.Trim() ?? string.Empty;

        if (keyName.Length == 0)
        {
            throw new ValidationException("A key name is required.");
        }

        if (!IsValidKeyName(keyName))
        {
            throw new ValidationException("A key name may only contain letters, digits, '.', '-' and '_'.");
        }

        if (!await _projects.ExistsAsync(projectId, cancellationToken))
        {
            throw new NotFoundException($"Project '{projectId}' was not found.");
        }

        if (await _keys.KeyNameExistsAsync(projectId, keyName, cancellationToken))
        {
            throw new ConflictException($"A key named '{keyName}' already exists in this project.");
        }

        var key = new TranslationKey(projectId, keyName, request.Description);

        await _keys.AddAsync(key, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(key);
    }

    public async Task<TranslationKeyDto?> UpdateAsync(
        Guid projectId,
        Guid keyId,
        UpdateTranslationKeyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = await _keys.GetAsync(projectId, keyId, cancellationToken);
        if (key is null)
        {
            return null;
        }

        key.Describe(request.Description);

        await _keys.UpdateAsync(key, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(key);
    }

    public async Task<bool> DeleteAsync(Guid projectId, Guid keyId, CancellationToken cancellationToken = default)
    {
        var key = await _keys.GetAsync(projectId, keyId, cancellationToken);
        if (key is null)
        {
            return false;
        }

        await _keys.RemoveAsync(key, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static bool IsValidKeyName(string keyName)
        => keyName.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-');

    private static TranslationKeyDto ToDto(TranslationKey key) => new(
        key.Id,
        key.ProjectId,
        key.KeyName,
        key.Description,
        key.CreatedAt,
        key.UpdatedAt);
}

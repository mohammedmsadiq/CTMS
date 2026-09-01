using CTMS.Application.Audit;
using CTMS.Application.Common;
using CTMS.Application.Projects;
using CTMS.Domain.Audit;
using CTMS.Domain.Projects;
using CTMS.Domain.Translations;

namespace CTMS.Application.Translations;

/// <summary>Use-case orchestration for translation keys.</summary>
public sealed class TranslationKeyService
{
    private const string SystemActor = "system";
    private const string AuditEntityType = "TranslationKey";
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private readonly ITranslationKeyRepository _keys;
    private readonly ITranslationStringRepository _strings;
    private readonly IProjectRepository _projects;
    private readonly IAuditRepository _audit;
    private readonly TranslationCacheInvalidator _invalidator;
    private readonly IUnitOfWork _unitOfWork;

    public TranslationKeyService(
        ITranslationKeyRepository keys,
        ITranslationStringRepository strings,
        IProjectRepository projects,
        IAuditRepository audit,
        TranslationCacheInvalidator invalidator,
        IUnitOfWork unitOfWork)
    {
        _keys = keys;
        _strings = strings;
        _projects = projects;
        _audit = audit;
        _invalidator = invalidator;
        _unitOfWork = unitOfWork;
    }

    public async Task<PagedResult<TranslationKeyDto>?> ListAsync(
        string applicationCode,
        string? category,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        var project = await ResolveApplicationAsync(applicationCode, cancellationToken);
        if (project is null)
        {
            return null;
        }

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

        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        var total = await _keys.CountByProjectAsync(project.Id, normalizedCategory, cancellationToken);
        var keys = await _keys.ListByProjectAsync(project.Id, normalizedCategory, skip, take, cancellationToken);

        return new PagedResult<TranslationKeyDto>(keys.Select(k => ToDto(k, project.Slug)).ToList(), total);
    }

    public async Task<TranslationKeyDto?> GetAsync(
        string applicationCode,
        Guid keyId,
        CancellationToken cancellationToken = default)
    {
        var project = await ResolveApplicationAsync(applicationCode, cancellationToken);
        if (project is null)
        {
            return null;
        }

        var key = await _keys.GetAsync(project.Id, keyId, cancellationToken);
        return key is null ? null : ToDto(key, project.Slug);
    }

    public async Task<TranslationKeyDto> CreateAsync(
        string applicationCode,
        CreateTranslationKeyRequest request,
        string actor,
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

        // Category is optional on the wire: derive one from the key-name prefix when it is blank.
        var category = string.IsNullOrWhiteSpace(request.Category)
            ? CategorySuggestion.FromKeyName(keyName)
            : request.Category.Trim();

        var project = await ResolveApplicationAsync(applicationCode, cancellationToken)
            ?? throw new NotFoundException($"Application '{applicationCode}' was not found.");

        if (await _keys.KeyNameExistsAsync(project.Id, keyName, cancellationToken))
        {
            throw new ConflictException($"A key named '{keyName}' already exists in this application.");
        }

        var createdBy = string.IsNullOrWhiteSpace(request.CreatedBy) ? Actor(actor) : request.CreatedBy!.Trim();
        var key = new TranslationKey(project.Id, keyName, category, createdBy, request.Description);

        await _keys.AddAsync(key, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(key, project.Slug);
    }

    public async Task<TranslationKeyDto?> UpdateAsync(
        string applicationCode,
        Guid keyId,
        UpdateTranslationKeyRequest request,
        string? actor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var project = await ResolveApplicationAsync(applicationCode, cancellationToken);
        if (project is null)
        {
            return null;
        }

        var key = await _keys.GetAsync(project.Id, keyId, cancellationToken);
        if (key is null)
        {
            return null;
        }

        if (request.Category is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Category))
            {
                throw new ValidationException("A category cannot be blank.");
            }

            key.SetCategory(request.Category);
        }

        if (request.Description is not null)
        {
            key.Describe(request.Description.Length == 0 ? null : request.Description);
        }

        var activeChanged = false;
        if (request.Active is { } active)
        {
            activeChanged = key.Active != active;
            key.SetActive(active);
        }

        await _keys.UpdateAsync(key, cancellationToken);

        if (activeChanged)
        {
            // Activating/deactivating a key adds it to / removes it from delivery — record it and
            // drop the delivery cache for the languages that had published values under this key.
            await _audit.AppendAsync(
                new AuditEntry(
                    project.Id, AuditEntityType, key.Id, AuditAction.Edited, Actor(actor),
                    detail: $"key {key.KeyName} {(key.Active ? "activated" : "deactivated")}"),
                cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (activeChanged)
        {
            await InvalidatePublishedLanguagesAsync(project, key.Id, cancellationToken);
        }

        return ToDto(key, project.Slug);
    }

    public async Task<bool?> DeleteAsync(
        string applicationCode,
        Guid keyId,
        string? actor = null,
        CancellationToken cancellationToken = default)
    {
        var project = await ResolveApplicationAsync(applicationCode, cancellationToken);
        if (project is null)
        {
            return null;
        }

        var key = await _keys.GetAsync(project.Id, keyId, cancellationToken);
        if (key is null)
        {
            return false;
        }

        // Which languages currently carry a published value under this key — those bundles go stale.
        var publishedLanguages = (await _strings.ListByKeyAsync(key.Id, cancellationToken))
            .Where(s => s.ReviewState == ReviewState.Published)
            .Select(s => s.LanguageCode)
            .ToList();

        await _keys.RemoveAsync(key, cancellationToken);
        await _audit.AppendAsync(
            new AuditEntry(
                project.Id, AuditEntityType, key.Id, AuditAction.Deleted, Actor(actor),
                detail: $"key {key.KeyName} deleted"),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (publishedLanguages.Count > 0)
        {
            await _invalidator.InvalidateAsync(project, publishedLanguages, cancellationToken);
        }

        return true;
    }

    private async Task InvalidatePublishedLanguagesAsync(
        Project project, Guid keyId, CancellationToken cancellationToken)
    {
        var languages = (await _strings.ListByKeyAsync(keyId, cancellationToken))
            .Where(s => s.ReviewState == ReviewState.Published)
            .Select(s => s.LanguageCode)
            .ToList();

        if (languages.Count > 0)
        {
            await _invalidator.InvalidateAsync(project, languages, cancellationToken);
        }
    }

    private Task<Project?> ResolveApplicationAsync(string applicationCode, CancellationToken cancellationToken)
        => _projects.GetBySlugAsync(Slug.From(applicationCode ?? string.Empty), cancellationToken);

    private static string Actor(string? actor) => string.IsNullOrWhiteSpace(actor) ? SystemActor : actor.Trim();

    private static bool IsValidKeyName(string keyName)
        => keyName.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-');

    private static TranslationKeyDto ToDto(TranslationKey key, string applicationCode) => new(
        key.Id,
        applicationCode,
        key.KeyName,
        key.Category,
        key.Description,
        key.Active,
        key.CreatedBy,
        key.CreatedAt,
        key.UpdatedAt);
}

using CTMS.Application.Audit;
using CTMS.Application.Common;
using CTMS.Application.Languages;
using CTMS.Application.Projects;
using CTMS.Application.Webhooks;
using CTMS.Domain.Audit;
using CTMS.Domain.Projects;
using CTMS.Domain.Translations;

namespace CTMS.Application.Translations;

/// <summary>Use-case orchestration for translation strings and the review workflow.</summary>
public sealed class TranslationStringService
{
    private const string SystemActor = "system";
    private const string AuditEntityType = "TranslationString";
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private readonly ITranslationStringRepository _strings;
    private readonly ITranslationKeyRepository _keys;
    private readonly ILanguageRepository _languages;
    private readonly IProjectRepository _projects;
    private readonly IAuditRepository _audit;
    private readonly TranslationCacheInvalidator _invalidator;
    private readonly IWebhookPublisher _webhooks;
    private readonly IUnitOfWork _unitOfWork;

    public TranslationStringService(
        ITranslationStringRepository strings,
        ITranslationKeyRepository keys,
        ILanguageRepository languages,
        IProjectRepository projects,
        IAuditRepository audit,
        TranslationCacheInvalidator invalidator,
        IWebhookPublisher webhooks,
        IUnitOfWork unitOfWork)
    {
        _strings = strings;
        _keys = keys;
        _languages = languages;
        _projects = projects;
        _audit = audit;
        _invalidator = invalidator;
        _webhooks = webhooks;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<TranslationStringDto>?> ListByKeyAsync(
        string applicationCode,
        Guid keyId,
        CancellationToken cancellationToken = default)
    {
        var project = await ResolveApplicationAsync(applicationCode, cancellationToken);
        if (project is null || await _keys.GetAsync(project.Id, keyId, cancellationToken) is null)
        {
            return null;
        }

        var strings = await _strings.ListByKeyAsync(keyId, cancellationToken);
        return strings.Select(ToDto).ToList();
    }

    public async Task<TranslationStringDto?> GetAsync(
        string applicationCode,
        Guid keyId,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        var project = await ResolveApplicationAsync(applicationCode, cancellationToken);
        if (project is null || await _keys.GetAsync(project.Id, keyId, cancellationToken) is null)
        {
            return null;
        }

        var translationString = await _strings.GetAsync(keyId, NormalizeCode(languageCode), cancellationToken);
        return translationString is null ? null : ToDto(translationString);
    }

    /// <summary>
    /// One page of every string in an application, newest-updated first, optionally filtered by
    /// review state. Returns <c>null</c> when the application is unknown.
    /// </summary>
    public async Task<PagedResult<TranslationStringDto>?> ListByProjectAsync(
        string applicationCode,
        string? reviewState,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        var stateFilter = ParseReviewState(reviewState);

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

        var keyIds = (await _keys.ListByProjectsAsync([project.Id], cancellationToken))
            .Select(k => k.Id)
            .ToList();

        var page = await _strings.ListByKeysAndStateAsync(keyIds, stateFilter, skip, take, cancellationToken);

        return new PagedResult<TranslationStringDto>(page.Items.Select(ToDto).ToList(), page.Total);
    }

    private static ReviewState? ParseReviewState(string? reviewState)
    {
        if (string.IsNullOrWhiteSpace(reviewState))
        {
            return null;
        }

        var candidate = reviewState.Trim();
        if (!int.TryParse(candidate, out _)
            && Enum.TryParse<ReviewState>(candidate, ignoreCase: false, out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new ValidationException(
            $"'{reviewState}' is not a valid review state. Expected one of: {string.Join(", ", Enum.GetNames<ReviewState>())}.");
    }

    public async Task<UpsertTranslationStringResult> UpsertAsync(
        string applicationCode,
        Guid keyId,
        string languageCode,
        UpsertTranslationStringRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(request.Value))
        {
            throw new ValidationException("A translation value is required.");
        }

        var project = await ResolveApplicationAsync(applicationCode, cancellationToken)
            ?? throw new NotFoundException($"Application '{applicationCode}' was not found.");

        var key = await _keys.GetAsync(project.Id, keyId, cancellationToken)
            ?? throw new NotFoundException($"Translation key '{keyId}' was not found in application '{project.Slug}'.");

        var language = await RequireEnabledLanguageAsync(project, languageCode, cancellationToken);

        var actor = string.IsNullOrWhiteSpace(request.UpdatedBy) ? SystemActor : request.UpdatedBy!;
        var existing = await _strings.GetAsync(key.Id, language, cancellationToken);

        if (existing is null)
        {
            var created = new TranslationString(key.Id, language, request.Value, actor);
            await _strings.AddAsync(created, cancellationToken);
            await _audit.AppendAsync(
                new AuditEntry(
                    project.Id,
                    AuditEntityType,
                    created.Id,
                    AuditAction.Created,
                    actor,
                    toState: created.ReviewState,
                    newValue: created.Value),
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new UpsertTranslationStringResult(ToDto(created), Created: true);
        }

        if (string.Equals(existing.Value, request.Value, StringComparison.Ordinal))
        {
            // No change — nothing to persist or audit.
            return new UpsertTranslationStringResult(ToDto(existing), Created: false);
        }

        var oldValue = existing.Value;
        var fromState = existing.ReviewState;
        existing.Edit(request.Value, actor);
        await _strings.UpdateAsync(existing, cancellationToken);
        await _audit.AppendAsync(
            new AuditEntry(
                project.Id,
                AuditEntityType,
                existing.Id,
                AuditAction.Edited,
                actor,
                fromState,
                existing.ReviewState,
                oldValue: oldValue,
                newValue: existing.Value),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (fromState == ReviewState.Published)
        {
            // Published content just changed (and dropped back to NeedsReview) — drop the delivery cache.
            await _invalidator.InvalidateAsync(project, [existing.LanguageCode], cancellationToken);
        }

        return new UpsertTranslationStringResult(ToDto(existing), Created: false);
    }

    public async Task<TranslationStringDto?> ReviewAsync(
        string applicationCode,
        Guid keyId,
        string languageCode,
        string action,
        string reviewedBy,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reviewedBy))
        {
            throw new ValidationException("A reviewer is required.");
        }

        var (target, auditAction) = ResolveReviewAction(action);

        var project = await ResolveApplicationAsync(applicationCode, cancellationToken);
        if (project is null || await _keys.GetAsync(project.Id, keyId, cancellationToken) is null)
        {
            return null;
        }

        var translationString = await _strings.GetAsync(keyId, NormalizeCode(languageCode), cancellationToken);
        if (translationString is null)
        {
            return null;
        }

        var fromState = translationString.ReviewState;
        translationString.ChangeReviewState(target, reviewedBy);
        await _strings.UpdateAsync(translationString, cancellationToken);
        await _audit.AppendAsync(
            new AuditEntry(
                project.Id,
                AuditEntityType,
                translationString.Id,
                auditAction,
                reviewedBy,
                fromState,
                translationString.ReviewState),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (fromState == ReviewState.Published || translationString.ReviewState == ReviewState.Published)
        {
            // A string entered or left Published — the assembled delivery map may have changed.
            await _invalidator.InvalidateAsync(project, [translationString.LanguageCode], cancellationToken);
        }

        if (target == ReviewState.Published)
        {
            _webhooks.Enqueue(project.Slug, [translationString.LanguageCode]);
        }

        return ToDto(translationString);
    }

    /// <summary>
    /// Applies <paramref name="request"/>.<c>Action</c> to every string of the application that
    /// matches the optional language / category / key-id filters and is in a state the action is
    /// legal from. Illegal transitions are skipped, not errored. Requires at least one filter.
    /// </summary>
    public async Task<ReviewBulkResult> ReviewBulkAsync(
        string applicationCode,
        ReviewBulkRequest request,
        string reviewedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(reviewedBy))
        {
            throw new ValidationException("A reviewer is required.");
        }

        var normalizedCategory = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim();
        var keyIdFilter = request.KeyIds is { Count: > 0 } ? request.KeyIds.ToHashSet() : null;
        var languageFilterRaw = string.IsNullOrWhiteSpace(request.Language) ? null : NormalizeCode(request.Language);

        if (normalizedCategory is null && keyIdFilter is null && languageFilterRaw is null)
        {
            throw new ValidationException(
                "A bulk review needs at least one filter: 'language', 'category' or 'keyIds'.");
        }

        var (target, auditAction) = ResolveReviewAction(request.Action);

        var project = await ResolveApplicationAsync(applicationCode, cancellationToken)
            ?? throw new NotFoundException($"Application '{applicationCode}' was not found.");

        string? languageFilter = null;
        if (languageFilterRaw is not null)
        {
            var language = await _languages.GetByCodeAsync(languageFilterRaw, cancellationToken)
                ?? throw new NotFoundException($"Language '{languageFilterRaw}' is not registered.");
            languageFilter = language.Code;
        }

        var by = reviewedBy.Trim();

        var keyIds = (await _keys.ListByProjectsAsync([project.Id], cancellationToken))
            .Where(k => normalizedCategory is null
                || string.Equals(k.Category, normalizedCategory, StringComparison.OrdinalIgnoreCase))
            .Where(k => keyIdFilter is null || keyIdFilter.Contains(k.Id))
            .Select(k => k.Id)
            .ToList();

        var strings = (await _strings.ListByKeyIdsAsync(keyIds, cancellationToken))
            .Where(s => languageFilter is null
                || string.Equals(s.LanguageCode, languageFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var transitioned = 0;
        var skipped = 0;
        var affectedLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var translationString in strings)
        {
            var fromState = translationString.ReviewState;
            try
            {
                translationString.ChangeReviewState(target, by);
            }
            catch (InvalidReviewTransitionException)
            {
                skipped++;
                continue;
            }

            await _strings.UpdateAsync(translationString, cancellationToken);
            await _audit.AppendAsync(
                new AuditEntry(
                    project.Id,
                    AuditEntityType,
                    translationString.Id,
                    auditAction,
                    by,
                    fromState,
                    translationString.ReviewState),
                cancellationToken);
            transitioned++;

            if (fromState == ReviewState.Published || translationString.ReviewState == ReviewState.Published)
            {
                affectedLanguages.Add(translationString.LanguageCode);
            }
        }

        if (transitioned > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        if (affectedLanguages.Count > 0)
        {
            await _invalidator.InvalidateAsync(project, affectedLanguages.ToList(), cancellationToken);

            if (target == ReviewState.Published)
            {
                _webhooks.Enqueue(project.Slug, affectedLanguages.ToList());
            }
        }

        return new ReviewBulkResult(transitioned, skipped);
    }

    private async Task<string> RequireEnabledLanguageAsync(
        Project project,
        string languageCode,
        CancellationToken cancellationToken)
    {
        var code = NormalizeCode(languageCode);
        if (code.Length == 0)
        {
            throw new ValidationException("A language code is required.");
        }

        var language = await _languages.GetByCodeAsync(code, cancellationToken)
            ?? throw new NotFoundException($"Language '{code}' is not registered.");

        var enabled = project.EnabledLanguageCodes
            .Any(c => string.Equals(c, language.Code, StringComparison.OrdinalIgnoreCase));
        if (!enabled)
        {
            throw new NotFoundException(
                $"Language '{language.Code}' is not enabled for application '{project.Slug}'.");
        }

        return language.Code;
    }

    private static (ReviewState Target, AuditAction Audit) ResolveReviewAction(string action) =>
        action?.Trim().ToLowerInvariant() switch
        {
            "submit" => (ReviewState.NeedsReview, AuditAction.Submitted),
            "approve" => (ReviewState.Approved, AuditAction.Approved),
            "reject" => (ReviewState.Draft, AuditAction.Rejected),
            "reopen" => (ReviewState.NeedsReview, AuditAction.Reopened),
            "publish" => (ReviewState.Published, AuditAction.Published),
            _ => throw new ValidationException(
                $"Unknown review action '{action}'. Expected 'submit', 'approve', 'reject', 'reopen' or 'publish'."),
        };

    private Task<Project?> ResolveApplicationAsync(string applicationCode, CancellationToken cancellationToken)
        => _projects.GetBySlugAsync(Slug.From(applicationCode ?? string.Empty), cancellationToken);

    private static string NormalizeCode(string? code)
        => string.IsNullOrWhiteSpace(code)
            ? string.Empty
            : string.Join(' ', code.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static TranslationStringDto ToDto(TranslationString s) => new(
        s.Id,
        s.TranslationKeyId,
        s.LanguageCode,
        s.Value,
        s.ReviewState.ToString(),
        s.UpdatedBy,
        s.CreatedAt,
        s.UpdatedAt);
}

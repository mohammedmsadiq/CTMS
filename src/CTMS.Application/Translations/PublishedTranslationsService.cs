using CTMS.Application.Audit;
using CTMS.Application.Common;
using CTMS.Application.Languages;
using CTMS.Application.Projects;
using CTMS.Application.Webhooks;
using CTMS.Domain.Audit;
using CTMS.Domain.Languages;
using CTMS.Domain.Projects;
using CTMS.Domain.Translations;

namespace CTMS.Application.Translations;

/// <summary>
/// Assembles published translations on demand (there are no versioned bundles) and powers the
/// management grid / dashboard / missing screens and bulk publish.
/// </summary>
/// <remarks>
/// <para><b>Assembly order</b> for <c>(application, language)</c>:</para>
/// <list type="number">
///   <item>resolve the application (404 unknown/inactive) and language (404 unknown/inactive, or
///   not in the application's enabled set);</item>
///   <item>gather <see cref="ReviewState.Published"/> strings for this application's keys plus every
///   shared application's keys; on a key-name collision the application-specific value wins;</item>
///   <item>for keys still missing a published value in the language, walk the
///   <see cref="Language.FallbackCode"/> chain (cycle-guarded) and take the first published value;
///   a key with no published value anywhere is omitted;</item>
///   <item>return a flat <c>keyName → value</c> map ordered by key.</item>
/// </list>
/// <para><b>"Translated"</b> for coverage / missing means a <see cref="TranslationString"/> exists
/// in any non-<see cref="ReviewState.Draft"/> state.</para>
/// </remarks>
public sealed class PublishedTranslationsService
{
    private const string SystemActor = "system";
    private const string AuditEntityType = "TranslationString";
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private readonly IProjectRepository _projects;
    private readonly ILanguageRepository _languages;
    private readonly ITranslationKeyRepository _keys;
    private readonly ITranslationStringRepository _strings;
    private readonly IAuditRepository _audit;
    private readonly IPublishedTranslationsCache _cache;
    private readonly TranslationCacheInvalidator _invalidator;
    private readonly IWebhookPublisher _webhooks;
    private readonly IUnitOfWork _unitOfWork;

    public PublishedTranslationsService(
        IProjectRepository projects,
        ILanguageRepository languages,
        ITranslationKeyRepository keys,
        ITranslationStringRepository strings,
        IAuditRepository audit,
        IPublishedTranslationsCache cache,
        TranslationCacheInvalidator invalidator,
        IWebhookPublisher webhooks,
        IUnitOfWork unitOfWork)
    {
        _projects = projects;
        _languages = languages;
        _keys = keys;
        _strings = strings;
        _audit = audit;
        _cache = cache;
        _invalidator = invalidator;
        _webhooks = webhooks;
        _unitOfWork = unitOfWork;
    }

    // ---- Client delivery: assemble-on-demand -------------------------------------------------

    public async Task<PublishedTranslationsView?> GetPublishedAsync(
        string applicationCode,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        var app = await _projects.GetBySlugAsync(Slug.From(applicationCode ?? string.Empty), cancellationToken);
        if (app is null || !app.Active)
        {
            return null;
        }

        var languagesByCode = (await _languages.ListAllAsync(cancellationToken))
            .ToDictionary(l => l.Code, StringComparer.OrdinalIgnoreCase);

        var requested = (languageCode ?? string.Empty).Trim();
        if (!languagesByCode.TryGetValue(requested, out var language) || !language.Active)
        {
            return null;
        }

        if (!app.EnabledLanguageCodes.Any(c => string.Equals(c, language.Code, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var cached = await _cache.GetAsync(app.Slug, language.Code, cancellationToken);
        if (cached is not null)
        {
            return new PublishedTranslationsView(app.Slug, language.Code, cached.Translations, cached.Hash);
        }

        var map = await AssembleAsync(app, language.Code, languagesByCode, includeApproved: false, cancellationToken);
        var hash = TranslationContentHash.Compute(map);

        await _cache.SetAsync(app.Slug, language.Code, new CachedTranslations(map, hash), cancellationToken);
        return new PublishedTranslationsView(app.Slug, language.Code, map, hash);
    }

    /// <param name="includeApproved">
    /// When <c>true</c> the assembly treats <see cref="ReviewState.Approved"/> strings as if they
    /// were already <see cref="ReviewState.Published"/> — the "what publish would deliver" view used
    /// by the publish preview.
    /// </param>
    private async Task<IReadOnlyDictionary<string, string>> AssembleAsync(
        Project app,
        string languageCode,
        IReadOnlyDictionary<string, Language> languagesByCode,
        bool includeApproved,
        CancellationToken cancellationToken)
    {
        var appKeys = (await _keys.ListByProjectsAsync([app.Id], cancellationToken))
            .Where(k => k.Active)
            .OrderBy(k => k.KeyName, StringComparer.Ordinal)
            .ToList();

        var sharedProjectIds = (await _projects.ListSharedAsync(cancellationToken))
            .Where(p => p.Id != app.Id)
            .Select(p => p.Id)
            .ToList();

        var sharedKeys = sharedProjectIds.Count == 0
            ? []
            : (await _keys.ListByProjectsAsync(sharedProjectIds, cancellationToken))
                .Where(k => k.Active)
                .OrderBy(k => k.KeyName, StringComparer.Ordinal)
                .ToList();

        var allKeyIds = appKeys.Select(k => k.Id).Concat(sharedKeys.Select(k => k.Id)).ToList();
        var candidateStrings = includeApproved
            ? (await _strings.ListByKeyIdsAsync(allKeyIds, cancellationToken))
                .Where(s => s.ReviewState is ReviewState.Published or ReviewState.Approved)
            : await _strings.ListPublishedByKeyIdsAsync(allKeyIds, cancellationToken);
        var publishedByKey = candidateStrings
            .GroupBy(s => s.TranslationKeyId)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(s => s.LanguageCode, s => s.Value, StringComparer.OrdinalIgnoreCase)
                    as IReadOnlyDictionary<string, string>);

        string? Resolve(Guid keyId)
        {
            if (!publishedByKey.TryGetValue(keyId, out var byLang))
            {
                return null;
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = languageCode;
            while (current is not null && visited.Add(current))
            {
                if (byLang.TryGetValue(current, out var value))
                {
                    return value;
                }

                current = languagesByCode.TryGetValue(current, out var lang) ? lang.FallbackCode : null;
            }

            return null;
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var key in appKeys)
        {
            if (Resolve(key.Id) is { } value)
            {
                result[key.KeyName] = value;
            }
        }

        foreach (var key in sharedKeys)
        {
            if (result.ContainsKey(key.KeyName))
            {
                continue; // application-specific value wins over a shared one
            }

            if (Resolve(key.Id) is { } value)
            {
                result[key.KeyName] = value;
            }
        }

        return result
            .OrderBy(e => e.Key, StringComparer.Ordinal)
            .ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
    }

    // ---- Management: grid ------------------------------------------------------------------

    /// <param name="status">
    /// Optional review-state filter (one of the four <see cref="ReviewState"/> names; an invalid
    /// value is a <see cref="ValidationException"/>). When set, a row is included only if it has at
    /// least one cell in that state — but the row still carries <em>all</em> its cells so the grid
    /// stays coherent.
    /// </param>
    public async Task<PagedResult<TranslationRowDto>?> GetGridAsync(
        string? applicationCode,
        string? category,
        string? languageCode,
        string? search,
        int skip,
        int take,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var statusFilter = ParseGridStatus(status);

        var scope = await ResolveScopeAsync(applicationCode, cancellationToken);
        if (scope is null)
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

        var columns = ResolveColumns(scope, languageCode);

        // For a single (non-shared) application the grid merges in every shared application's keys,
        // tagging each row's cells with their provenance. Elsewhere every key is its own app's.
        var tagged = await BuildTaggedKeysAsync(scope, cancellationToken);

        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        var keys = tagged
            .Where(t => normalizedCategory is null
                || string.Equals(t.Key.Category, normalizedCategory, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var stringsByKey = (await _strings.ListByKeyIdsAsync(keys.Select(t => t.Key.Id).ToList(), cancellationToken))
            .GroupBy(s => s.TranslationKeyId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (term is not null)
        {
            keys = keys
                .Where(t => t.Key.KeyName.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (stringsByKey.TryGetValue(t.Key.Id, out var ss)
                        && ss.Any(s => s.Value.Contains(term, StringComparison.OrdinalIgnoreCase))))
                .ToList();
        }

        if (statusFilter is { } wantedState)
        {
            keys = keys
                .Where(t => stringsByKey.TryGetValue(t.Key.Id, out var ss)
                    && ss.Any(s => s.ReviewState == wantedState))
                .ToList();
        }

        keys.Sort((a, b) => string.CompareOrdinal(a.Key.KeyName, b.Key.KeyName));

        var total = keys.Count;
        var rows = keys
            .Skip(skip)
            .Take(take)
            .Select(t =>
            {
                var byLang = stringsByKey.TryGetValue(t.Key.Id, out var ss)
                    ? ss.ToDictionary(s => s.LanguageCode, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, TranslationString>(StringComparer.OrdinalIgnoreCase);

                var values = new Dictionary<string, TranslationValueDto>(StringComparer.Ordinal);
                foreach (var column in columns)
                {
                    if (byLang.TryGetValue(column, out var s))
                    {
                        values[column] = new TranslationValueDto(s.Value, s.ReviewState.ToString(), t.Source);
                    }
                }

                return new TranslationRowDto(t.Key.Id, t.Key.KeyName, t.Key.Category, t.Key.Description, values);
            })
            .ToList();

        return new PagedResult<TranslationRowDto>(rows, total);
    }

    private async Task<IReadOnlyList<(Domain.Translations.TranslationKey Key, string Source)>> BuildTaggedKeysAsync(
        Scope scope,
        CancellationToken cancellationToken)
    {
        if (scope.SingleApplication is not { IsShared: false } app)
        {
            return scope.Keys.Select(k => (k, "app")).ToList();
        }

        var ownedNames = scope.Keys.Select(k => k.KeyName).ToHashSet(StringComparer.Ordinal);
        var tagged = scope.Keys.Select(k => (Key: k, Source: "app")).ToList();

        var sharedProjects = (await _projects.ListSharedAsync(cancellationToken))
            .Where(p => p.Id != app.Id)
            .ToList();
        if (sharedProjects.Count == 0)
        {
            return tagged;
        }

        var slugById = sharedProjects.ToDictionary(p => p.Id, p => p.Slug);
        var sharedKeys = (await _keys.ListByProjectsAsync(sharedProjects.Select(p => p.Id).ToList(), cancellationToken))
            .Where(k => k.Active && !ownedNames.Contains(k.KeyName));

        foreach (var key in sharedKeys)
        {
            tagged.Add((key, $"shared:{slugById[key.ProjectId]}"));
        }

        return tagged;
    }

    private static ReviewState? ParseGridStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        var candidate = status.Trim();
        if (!int.TryParse(candidate, out _)
            && Enum.TryParse<ReviewState>(candidate, ignoreCase: false, out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new ValidationException(
            $"'{status}' is not a valid review state. Expected one of: {string.Join(", ", Enum.GetNames<ReviewState>())}.");
    }

    // ---- Management: publish preview -----------------------------------------------------

    /// <summary>
    /// The diff a <c>POST /api/translations/publish</c> for the same <c>(application, language)</c>
    /// would make to the delivered map: the application's <see cref="ReviewState.Approved"/> strings
    /// are treated as if already published and the result is compared with what is delivered today.
    /// <paramref name="languageCode"/> is <b>required</b>. Returns <c>null</c> (→ 404) for an unknown
    /// or inactive application/language, or a language not enabled for the application.
    /// </summary>
    public async Task<PublishPreviewResponse?> GetPublishPreviewAsync(
        string? applicationCode,
        string? languageCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(applicationCode))
        {
            throw new ValidationException("An application is required.");
        }

        if (string.IsNullOrWhiteSpace(languageCode))
        {
            throw new ValidationException("A language is required.");
        }

        var app = await _projects.GetBySlugAsync(Slug.From(applicationCode), cancellationToken);
        if (app is null || !app.Active)
        {
            return null;
        }

        var languagesByCode = (await _languages.ListAllAsync(cancellationToken))
            .ToDictionary(l => l.Code, StringComparer.OrdinalIgnoreCase);

        if (!languagesByCode.TryGetValue(languageCode.Trim(), out var language) || !language.Active)
        {
            return null;
        }

        if (!app.EnabledLanguageCodes.Any(c => string.Equals(c, language.Code, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var current = await AssembleAsync(app, language.Code, languagesByCode, includeApproved: false, cancellationToken);
        var hypothetical = await AssembleAsync(app, language.Code, languagesByCode, includeApproved: true, cancellationToken);

        var changes = new List<PublishPreviewChange>();
        foreach (var (key, newValue) in hypothetical.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (!current.TryGetValue(key, out var currentValue))
            {
                changes.Add(new PublishPreviewChange(key, null, newValue, "added"));
            }
            else if (!string.Equals(currentValue, newValue, StringComparison.Ordinal))
            {
                changes.Add(new PublishPreviewChange(key, currentValue, newValue, "changed"));
            }
        }

        return new PublishPreviewResponse(
            app.Slug,
            language.Code,
            changes,
            changes.Count(c => c.Kind == "added"),
            changes.Count(c => c.Kind == "changed"));
    }

    // ---- Management: categories ----------------------------------------------------------

    public async Task<IReadOnlyList<string>?> GetCategoriesAsync(
        string? applicationCode,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(applicationCode, cancellationToken);
        if (scope is null)
        {
            return null;
        }

        return scope.Keys
            .Select(k => k.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();
    }

    // ---- Management: dashboard ---------------------------------------------------------

    public async Task<DashboardResponse?> GetDashboardAsync(
        string? applicationCode,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(applicationCode, cancellationToken);
        if (scope is null)
        {
            return null;
        }

        var columns = ResolveColumns(scope, languageCode: null);
        var languageNames = (await _languages.ListAllAsync(cancellationToken))
            .ToDictionary(l => l.Code, l => l.Name, StringComparer.OrdinalIgnoreCase);

        var translatedByLang = (await _strings.ListByKeyIdsAsync(scope.Keys.Select(k => k.Id).ToList(), cancellationToken))
            .Where(s => s.ReviewState != ReviewState.Draft)
            .GroupBy(s => s.LanguageCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(s => s.TranslationKeyId).Distinct().Count(), StringComparer.OrdinalIgnoreCase);

        var keyCount = scope.Keys.Count;

        var coverage = columns
            .Select(code =>
            {
                var translated = translatedByLang.GetValueOrDefault(code, 0);
                var percent = keyCount == 0 ? 0d : Math.Round(translated * 100d / keyCount, 1);
                return new LanguageCoverageDto(
                    code,
                    languageNames.GetValueOrDefault(code, code),
                    translated,
                    keyCount,
                    percent,
                    keyCount - translated);
            })
            .OrderBy(c => c.LanguageCode, StringComparer.Ordinal)
            .ToList();

        return new DashboardResponse(
            scope.Projects.Count,
            columns.Count,
            keyCount,
            coverage,
            coverage.Sum(c => c.MissingCount));
    }

    // ---- Management: missing --------------------------------------------------------

    public async Task<PagedResult<MissingTranslationDto>?> GetMissingAsync(
        string? applicationCode,
        string? languageCode,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(applicationCode, cancellationToken);
        if (scope is null)
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

        var targetLanguages = ResolveColumns(scope, languageCode);

        var translatedByKey = (await _strings.ListByKeyIdsAsync(scope.Keys.Select(k => k.Id).ToList(), cancellationToken))
            .Where(s => s.ReviewState != ReviewState.Draft)
            .GroupBy(s => s.TranslationKeyId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(s => s.LanguageCode).ToHashSet(StringComparer.OrdinalIgnoreCase));

        var rows = scope.Keys
            .OrderBy(k => k.KeyName, StringComparer.Ordinal)
            .Select(k =>
            {
                var have = translatedByKey.GetValueOrDefault(k.Id) ?? [];
                var missing = targetLanguages.Where(l => !have.Contains(l)).ToList();
                return (Key: k, Missing: missing);
            })
            .Where(x => x.Missing.Count > 0)
            .ToList();

        var total = rows.Count;
        var page = rows
            .Skip(skip)
            .Take(take)
            .Select(x => new MissingTranslationDto(x.Key.Id, x.Key.KeyName, x.Key.Category, x.Missing))
            .ToList();

        return new PagedResult<MissingTranslationDto>(page, total);
    }

    // ---- Management: bulk publish -------------------------------------------------

    public async Task<PublishTranslationsResult> BulkPublishAsync(
        PublishTranslationsRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var app = await _projects.GetBySlugAsync(Slug.From(request.Application ?? string.Empty), cancellationToken)
            ?? throw new NotFoundException($"Application '{request.Application}' was not found.");

        string? language = null;
        if (!string.IsNullOrWhiteSpace(request.Language))
        {
            var resolved = await _languages.GetByCodeAsync(request.Language.Trim(), cancellationToken)
                ?? throw new NotFoundException($"Language '{request.Language}' is not registered.");
            language = resolved.Code;
        }

        var by = string.IsNullOrWhiteSpace(actor) ? SystemActor : actor.Trim();

        var keyIds = (await _keys.ListByProjectsAsync([app.Id], cancellationToken))
            .Where(k => k.Active)
            .Select(k => k.Id)
            .ToList();

        var approved = await _strings.ListApprovedByKeyIdsAsync(keyIds, language, cancellationToken);

        foreach (var s in approved)
        {
            s.ChangeReviewState(ReviewState.Published, by);
            await _strings.UpdateAsync(s, cancellationToken);
            await _audit.AppendAsync(
                new AuditEntry(
                    app.Id,
                    AuditEntityType,
                    s.Id,
                    AuditAction.Published,
                    by,
                    ReviewState.Approved,
                    ReviewState.Published),
                cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var affectedLanguages = language is not null
            ? new List<string> { language }
            : approved.Select(s => s.LanguageCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        await _invalidator.InvalidateAsync(app, affectedLanguages, cancellationToken);

        if (approved.Count > 0)
        {
            _webhooks.Enqueue(app.Slug, affectedLanguages);
        }

        return new PublishTranslationsResult(approved.Count);
    }

    // ---- Scope resolution --------------------------------------------------------

    private sealed record Scope(
        IReadOnlyList<Project> Projects,
        IReadOnlyList<Domain.Translations.TranslationKey> Keys,
        Project? SingleApplication);

    private async Task<Scope?> ResolveScopeAsync(string? applicationCode, CancellationToken cancellationToken)
    {
        IReadOnlyList<Project> projects;
        Project? single = null;

        if (!string.IsNullOrWhiteSpace(applicationCode))
        {
            single = await _projects.GetBySlugAsync(Slug.From(applicationCode), cancellationToken);
            if (single is null)
            {
                return null;
            }

            projects = [single];
        }
        else
        {
            projects = await _projects.ListAsync(includeInactive: false, cancellationToken);
        }

        var keys = (await _keys.ListByProjectsAsync(projects.Select(p => p.Id).ToList(), cancellationToken))
            .Where(k => k.Active)
            .ToList();

        return new Scope(projects, keys, single);
    }

    private static IReadOnlyList<string> ResolveColumns(Scope scope, string? languageCode)
    {
        if (!string.IsNullOrWhiteSpace(languageCode))
        {
            return [languageCode.Trim()];
        }

        if (scope.SingleApplication is { } app)
        {
            return app.EnabledLanguageCodes.ToList();
        }

        // All applications: the union of every application's enabled languages.
        return scope.Projects
            .SelectMany(p => p.EnabledLanguageCodes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();
    }
}

using CTMS.Application.Audit;
using CTMS.Application.Common;
using CTMS.Application.Languages;
using CTMS.Application.Projects;
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
    private readonly IUnitOfWork _unitOfWork;

    public PublishedTranslationsService(
        IProjectRepository projects,
        ILanguageRepository languages,
        ITranslationKeyRepository keys,
        ITranslationStringRepository strings,
        IAuditRepository audit,
        IPublishedTranslationsCache cache,
        TranslationCacheInvalidator invalidator,
        IUnitOfWork unitOfWork)
    {
        _projects = projects;
        _languages = languages;
        _keys = keys;
        _strings = strings;
        _audit = audit;
        _cache = cache;
        _invalidator = invalidator;
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

        var map = await AssembleAsync(app, language.Code, languagesByCode, cancellationToken);
        var hash = TranslationContentHash.Compute(map);

        await _cache.SetAsync(app.Slug, language.Code, new CachedTranslations(map, hash), cancellationToken);
        return new PublishedTranslationsView(app.Slug, language.Code, map, hash);
    }

    private async Task<IReadOnlyDictionary<string, string>> AssembleAsync(
        Project app,
        string languageCode,
        IReadOnlyDictionary<string, Language> languagesByCode,
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
        var publishedByKey = (await _strings.ListPublishedByKeyIdsAsync(allKeyIds, cancellationToken))
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

    public async Task<PagedResult<TranslationRowDto>?> GetGridAsync(
        string? applicationCode,
        string? category,
        string? languageCode,
        string? search,
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

        var columns = ResolveColumns(scope, languageCode);

        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        var keys = scope.Keys
            .Where(k => normalizedCategory is null
                || string.Equals(k.Category, normalizedCategory, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var stringsByKey = (await _strings.ListByKeyIdsAsync(keys.Select(k => k.Id).ToList(), cancellationToken))
            .GroupBy(s => s.TranslationKeyId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (term is not null)
        {
            keys = keys
                .Where(k => k.KeyName.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (stringsByKey.TryGetValue(k.Id, out var ss)
                        && ss.Any(s => s.Value.Contains(term, StringComparison.OrdinalIgnoreCase))))
                .ToList();
        }

        keys.Sort((a, b) => string.CompareOrdinal(a.KeyName, b.KeyName));

        var total = keys.Count;
        var rows = keys
            .Skip(skip)
            .Take(take)
            .Select(k =>
            {
                var byLang = stringsByKey.TryGetValue(k.Id, out var ss)
                    ? ss.ToDictionary(s => s.LanguageCode, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, TranslationString>(StringComparer.OrdinalIgnoreCase);

                var values = new Dictionary<string, TranslationValueDto>(StringComparer.Ordinal);
                foreach (var column in columns)
                {
                    if (byLang.TryGetValue(column, out var s))
                    {
                        values[column] = new TranslationValueDto(s.Value, s.ReviewState.ToString());
                    }
                }

                return new TranslationRowDto(k.Id, k.KeyName, k.Category, k.Description, values);
            })
            .ToList();

        return new PagedResult<TranslationRowDto>(rows, total);
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

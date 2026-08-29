using CTMS.Application.Audit;
using CTMS.Application.Common;
using CTMS.Application.Locales;
using CTMS.Application.Projects;
using CTMS.Domain.Audit;
using CTMS.Domain.Translations;

namespace CTMS.Application.Translations;

/// <summary>
/// Use-case orchestration for immutable published bundles. <see cref="PublishAsync"/> snapshots
/// the strings that are already in <see cref="ReviewState.Published"/> for a locale into a new,
/// versioned <see cref="TranslationBundle"/>; it never changes any string's review state. Strings
/// reach <see cref="ReviewState.Published"/> beforehand through the review <c>publish</c> action.
/// </summary>
public sealed class TranslationBundleService
{
    private const string SystemActor = "system";
    private const string AuditEntityType = "TranslationBundle";

    private readonly ITranslationBundleRepository _bundles;
    private readonly ITranslationStringRepository _strings;
    private readonly ITranslationKeyRepository _keys;
    private readonly ILocaleRepository _locales;
    private readonly IProjectRepository _projects;
    private readonly IAuditRepository _audit;
    private readonly IUnitOfWork _unitOfWork;

    public TranslationBundleService(
        ITranslationBundleRepository bundles,
        ITranslationStringRepository strings,
        ITranslationKeyRepository keys,
        ILocaleRepository locales,
        IProjectRepository projects,
        IAuditRepository audit,
        IUnitOfWork unitOfWork)
    {
        _bundles = bundles;
        _strings = strings;
        _keys = keys;
        _locales = locales;
        _projects = projects;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Snapshots every <see cref="ReviewState.Published"/> string for <paramref name="localeCode"/>
    /// into a new bundle version.
    /// </summary>
    /// <exception cref="NotFoundException">The project or the locale is unknown.</exception>
    /// <exception cref="ValidationException">The locale code is blank, or nothing is published yet.</exception>
    /// <exception cref="ConflictException">A concurrent publish already took the next version.</exception>
    public async Task<TranslationBundleDto> PublishAsync(
        Guid projectId,
        string localeCode,
        string publishedBy,
        CancellationToken cancellationToken = default)
    {
        var code = RequireLocaleCode(localeCode);
        var actor = string.IsNullOrWhiteSpace(publishedBy) ? SystemActor : publishedBy.Trim();

        if (!await _projects.ExistsAsync(projectId, cancellationToken))
        {
            throw new NotFoundException($"Project '{projectId}' was not found.");
        }

        var locale = await _locales.GetByCodeAsync(projectId, code, cancellationToken)
            ?? throw new NotFoundException($"Locale '{code}' was not found in project '{projectId}'.");

        var published = await _strings.ListByLocaleAndStateAsync(locale.Id, ReviewState.Published, cancellationToken);
        if (published.Count == 0)
        {
            throw new ValidationException(
                $"Locale '{locale.Code}' has nothing approved-and-published to snapshot.");
        }

        var keyNameById = (await _keys.ListByProjectAsync(projectId, 0, int.MaxValue, cancellationToken))
            .ToDictionary(k => k.Id, k => k.KeyName);

        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in published)
        {
            if (keyNameById.TryGetValue(s.TranslationKeyId, out var keyName))
            {
                entries[keyName] = s.Value;
            }
        }

        var nextVersion = ((await _bundles.GetLatestAsync(projectId, locale.Code, cancellationToken))?.Version ?? 0) + 1;

        var bundle = new TranslationBundle(projectId, locale.Code, nextVersion, entries, actor);
        await _bundles.InsertAsync(bundle, cancellationToken);

        await _audit.AppendAsync(
            new AuditEntry(
                projectId,
                AuditEntityType,
                bundle.Id,
                AuditAction.Published,
                actor,
                detail: $"{locale.Code} v{nextVersion}, {entries.Count} strings"),
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(bundle);
    }

    /// <summary>The latest bundle for a project's locale, or <c>null</c> if none has been published.</summary>
    public async Task<TranslationBundleDto?> GetLatestAsync(
        Guid projectId,
        string localeCode,
        CancellationToken cancellationToken = default)
    {
        var locale = await ResolveLocaleAsync(projectId, localeCode, cancellationToken);
        if (locale is null)
        {
            return null;
        }

        var bundle = await _bundles.GetLatestAsync(projectId, locale.Code, cancellationToken);
        return bundle is null ? null : ToDto(bundle);
    }

    /// <summary>A specific bundle version, or <c>null</c> if the project, locale, or version is unknown.</summary>
    public async Task<TranslationBundleDto?> GetByVersionAsync(
        Guid projectId,
        string localeCode,
        int version,
        CancellationToken cancellationToken = default)
    {
        var locale = await ResolveLocaleAsync(projectId, localeCode, cancellationToken);
        if (locale is null)
        {
            return null;
        }

        var bundle = await _bundles.GetByVersionAsync(projectId, locale.Code, version, cancellationToken);
        return bundle is null ? null : ToDto(bundle);
    }

    /// <summary>
    /// Every published version for a project's locale, oldest first, without the entries payload.
    /// <c>null</c> when the project or locale is unknown (distinct from an empty list).
    /// </summary>
    public async Task<IReadOnlyList<BundleVersionDto>?> ListVersionsAsync(
        Guid projectId,
        string localeCode,
        CancellationToken cancellationToken = default)
    {
        var locale = await ResolveLocaleAsync(projectId, localeCode, cancellationToken);
        if (locale is null)
        {
            return null;
        }

        var bundles = await _bundles.ListByProjectAndLocaleAsync(projectId, locale.Code, cancellationToken);
        return bundles
            .Select(b => new BundleVersionDto(b.Version, b.ETag, b.CreatedAt, b.CreatedBy, b.Entries.Count))
            .ToList();
    }

    private async Task<Domain.Locales.Locale?> ResolveLocaleAsync(
        Guid projectId,
        string localeCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(localeCode))
        {
            return null;
        }

        if (!await _projects.ExistsAsync(projectId, cancellationToken))
        {
            return null;
        }

        return await _locales.GetByCodeAsync(projectId, localeCode.Trim(), cancellationToken);
    }

    private static string RequireLocaleCode(string localeCode)
    {
        if (string.IsNullOrWhiteSpace(localeCode))
        {
            throw new ValidationException("A locale code is required.");
        }

        return localeCode.Trim();
    }

    private static TranslationBundleDto ToDto(TranslationBundle bundle) => new(
        bundle.Id,
        bundle.ProjectId,
        bundle.LocaleCode,
        bundle.Version,
        bundle.Entries,
        bundle.ETag,
        bundle.CreatedBy,
        bundle.CreatedAt);
}

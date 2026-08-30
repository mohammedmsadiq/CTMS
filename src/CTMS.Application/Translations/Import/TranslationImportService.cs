using CTMS.Application.Audit;
using CTMS.Application.Common;
using CTMS.Application.Languages;
using CTMS.Application.Projects;
using CTMS.Domain.Audit;
using CTMS.Domain.Translations;

namespace CTMS.Application.Translations.Import;

/// <summary>
/// Bulk-imports a translation file into one <c>(application, language)</c>: it parses the body
/// (see <see cref="TranslationFileParser"/>), creates any missing <see cref="TranslationKey"/>,
/// and upserts a <see cref="TranslationString"/> per parsed entry at the requested review state.
/// A <c>dryRun</c> computes the same plan without writing anything.
/// </summary>
public sealed class TranslationImportService
{
    private const string SystemActor = "system";
    private const string AuditEntityType = "TranslationString";
    private const int MaxKeysInResponse = 200;

    private readonly IProjectRepository _projects;
    private readonly ILanguageRepository _languages;
    private readonly ITranslationKeyRepository _keys;
    private readonly ITranslationStringRepository _strings;
    private readonly IAuditRepository _audit;
    private readonly TranslationCacheInvalidator _invalidator;
    private readonly IUnitOfWork _unitOfWork;

    public TranslationImportService(
        IProjectRepository projects,
        ILanguageRepository languages,
        ITranslationKeyRepository keys,
        ITranslationStringRepository strings,
        IAuditRepository audit,
        TranslationCacheInvalidator invalidator,
        IUnitOfWork unitOfWork)
    {
        _projects = projects;
        _languages = languages;
        _keys = keys;
        _strings = strings;
        _audit = audit;
        _invalidator = invalidator;
        _unitOfWork = unitOfWork;
    }

    public async Task<ImportTranslationsResult> ImportAsync(
        string applicationCode,
        ImportTranslationsRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var targetState = ParseImportStatus(request.Status);

        // Parse first so a malformed body is a 400 before any store round-trip.
        var entries = TranslationFileParser.Parse(request.Format, request.Content);

        var app = await _projects.GetBySlugAsync(Slug.From(applicationCode ?? string.Empty), cancellationToken)
            ?? throw new NotFoundException($"Application '{applicationCode}' was not found.");

        var languageCode = await ResolveEnabledLanguageAsync(app, request.Language, cancellationToken);

        var by = string.IsNullOrWhiteSpace(actor) ? SystemActor : actor.Trim();
        var requestedCategory = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim();

        var keysByName = (await _keys.ListByProjectsAsync([app.Id], cancellationToken))
            .ToDictionary(k => k.KeyName, k => k, StringComparer.Ordinal);

        var originalKeyIds = keysByName.Values.Select(k => k.Id).ToList();
        var stringByKeyId = (await _strings.ListByKeyIdsAsync(originalKeyIds, cancellationToken))
            .Where(s => string.Equals(s.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase))
            .GroupBy(s => s.TranslationKeyId)
            .ToDictionary(g => g.Key, g => g.First());

        var newKeys = new List<TranslationKey>();
        var newStrings = new List<TranslationString>();
        var changedStrings = new List<TranslationString>();
        var auditEntries = new List<AuditEntry>();
        var errors = new List<ImportError>();
        var keyNames = new List<string>();

        var createdKeys = 0;
        var createdStrings = 0;
        var updatedStrings = 0;
        var skipped = 0;
        var publishedTouched = false;

        foreach (var entry in entries)
        {
            var keyName = entry.Key.Trim();
            if (keyName.Length == 0 || !IsValidKeyName(keyName))
            {
                errors.Add(new ImportError(
                    null,
                    entry.Key,
                    "invalid key name; allowed characters are letters, digits, '.', '-' and '_'."));
                continue;
            }

            keyNames.Add(keyName);

            var keyIsNew = false;
            if (!keysByName.TryGetValue(keyName, out var key))
            {
                var category = requestedCategory ?? CategorySuggestion.FromKeyName(keyName);
                key = new TranslationKey(app.Id, keyName, category, by);
                keysByName[keyName] = key;
                newKeys.Add(key);
                createdKeys++;
                keyIsNew = true;
            }

            var existing = keyIsNew ? null : stringByKeyId.GetValueOrDefault(key.Id);
            if (existing is null)
            {
                var created = new TranslationString(key.Id, languageCode, entry.Value, by);
                DriveReviewState(created, targetState, by);
                newStrings.Add(created);
                stringByKeyId[key.Id] = created;
                createdStrings++;
                auditEntries.Add(new AuditEntry(
                    app.Id,
                    AuditEntityType,
                    created.Id,
                    AuditAction.Created,
                    by,
                    toState: created.ReviewState,
                    newValue: created.Value));
                continue;
            }

            var valueChanged = !string.Equals(existing.Value, entry.Value, StringComparison.Ordinal);
            var stateChanged = existing.ReviewState != targetState;
            if (!valueChanged && !stateChanged)
            {
                skipped++;
                continue;
            }

            var fromState = existing.ReviewState;
            var oldValue = existing.Value;

            if (valueChanged)
            {
                existing.Edit(entry.Value, by);
            }

            DriveReviewState(existing, targetState, by);
            changedStrings.Add(existing);
            updatedStrings++;

            if (valueChanged)
            {
                auditEntries.Add(new AuditEntry(
                    app.Id,
                    AuditEntityType,
                    existing.Id,
                    AuditAction.Edited,
                    by,
                    fromState,
                    existing.ReviewState,
                    oldValue: oldValue,
                    newValue: existing.Value));
            }

            if (fromState == ReviewState.Published || existing.ReviewState == ReviewState.Published)
            {
                publishedTouched = true;
            }
        }

        if (!request.DryRun)
        {
            foreach (var key in newKeys)
            {
                await _keys.AddAsync(key, cancellationToken);
            }

            foreach (var s in newStrings)
            {
                await _strings.AddAsync(s, cancellationToken);
            }

            foreach (var s in changedStrings)
            {
                await _strings.UpdateAsync(s, cancellationToken);
            }

            foreach (var e in auditEntries)
            {
                await _audit.AppendAsync(e, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (publishedTouched)
            {
                await _invalidator.InvalidateAsync(app, [languageCode], cancellationToken);
            }
        }

        var keys = keyNames.Distinct(StringComparer.Ordinal).Take(MaxKeysInResponse).ToList();
        return new ImportTranslationsResult(createdKeys, createdStrings, updatedStrings, skipped, errors, keys);
    }

    private async Task<string> ResolveEnabledLanguageAsync(
        Domain.Projects.Project app,
        string? requested,
        CancellationToken cancellationToken)
    {
        var code = (requested ?? string.Empty).Trim();
        if (code.Length == 0)
        {
            throw new ValidationException("A language code is required.");
        }

        var language = await _languages.GetByCodeAsync(code, cancellationToken)
            ?? throw new NotFoundException($"Language '{code}' is not registered.");

        if (!app.EnabledLanguageCodes.Any(c => string.Equals(c, language.Code, StringComparison.OrdinalIgnoreCase)))
        {
            throw new NotFoundException($"Language '{language.Code}' is not enabled for application '{app.Slug}'.");
        }

        return language.Code;
    }

    private static ReviewState ParseImportStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return ReviewState.Draft;
        }

        var candidate = status.Trim();
        if (string.Equals(candidate, nameof(ReviewState.Published), StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                "Import cannot set the 'Published' status; publish through the review workflow instead.");
        }

        if (Enum.TryParse<ReviewState>(candidate, ignoreCase: true, out var parsed)
            && parsed is ReviewState.Draft or ReviewState.NeedsReview or ReviewState.Approved)
        {
            return parsed;
        }

        throw new ValidationException(
            $"'{status}' is not a valid import status. Expected 'Draft', 'NeedsReview' or 'Approved'.");
    }

    private static void DriveReviewState(TranslationString subject, ReviewState target, string actor)
    {
        var guard = 0;
        while (subject.ReviewState != target && guard++ < 8)
        {
            subject.ChangeReviewState(StepToward(subject.ReviewState, target), actor);
        }
    }

    private static ReviewState StepToward(ReviewState current, ReviewState target) =>
        (int)target > (int)current
            ? current + 1
            : current switch
            {
                ReviewState.Published => ReviewState.NeedsReview,
                ReviewState.Approved => ReviewState.NeedsReview,
                ReviewState.NeedsReview => ReviewState.Draft,
                _ => target,
            };

    private static bool IsValidKeyName(string keyName)
        => keyName.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-');
}

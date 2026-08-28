using CTMS.Application.Common;
using CTMS.Application.Locales;
using CTMS.Domain.Translations;

namespace CTMS.Application.Translations;

/// <summary>Use-case orchestration for translation strings and the review workflow.</summary>
public sealed class TranslationStringService
{
    private const string SystemActor = "system";

    private readonly ITranslationStringRepository _strings;
    private readonly ITranslationKeyRepository _keys;
    private readonly ILocaleRepository _locales;
    private readonly IUnitOfWork _unitOfWork;

    public TranslationStringService(
        ITranslationStringRepository strings,
        ITranslationKeyRepository keys,
        ILocaleRepository locales,
        IUnitOfWork unitOfWork)
    {
        _strings = strings;
        _keys = keys;
        _locales = locales;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<TranslationStringDto>?> ListByKeyAsync(
        Guid projectId,
        Guid keyId,
        CancellationToken cancellationToken = default)
    {
        var key = await _keys.GetAsync(projectId, keyId, cancellationToken);
        if (key is null)
        {
            return null;
        }

        var codeByLocaleId = (await _locales.ListByProjectAsync(projectId, cancellationToken))
            .ToDictionary(locale => locale.Id, locale => locale.Code);

        var strings = await _strings.ListByKeyAsync(keyId, cancellationToken);

        return strings
            .Select(s => ToDto(s, codeByLocaleId.GetValueOrDefault(s.LocaleId, string.Empty)))
            .ToList();
    }

    public async Task<TranslationStringDto?> GetAsync(
        Guid projectId,
        Guid keyId,
        Guid localeId,
        CancellationToken cancellationToken = default)
    {
        var key = await _keys.GetAsync(projectId, keyId, cancellationToken);
        if (key is null)
        {
            return null;
        }

        var locale = await _locales.GetAsync(projectId, localeId, cancellationToken);
        if (locale is null)
        {
            return null;
        }

        var translationString = await _strings.GetAsync(keyId, localeId, cancellationToken);
        return translationString is null ? null : ToDto(translationString, locale.Code);
    }

    public async Task<UpsertTranslationStringResult> UpsertAsync(
        Guid projectId,
        Guid keyId,
        Guid localeId,
        UpsertTranslationStringRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(request.Value))
        {
            throw new ValidationException("A translation value is required.");
        }

        var key = await _keys.GetAsync(projectId, keyId, cancellationToken)
            ?? throw new NotFoundException($"Translation key '{keyId}' was not found in project '{projectId}'.");

        var locale = await _locales.GetAsync(projectId, localeId, cancellationToken)
            ?? throw new NotFoundException($"Locale '{localeId}' was not found in project '{projectId}'.");

        var actor = string.IsNullOrWhiteSpace(request.UpdatedBy) ? SystemActor : request.UpdatedBy!;
        var existing = await _strings.GetAsync(keyId, localeId, cancellationToken);

        if (existing is null)
        {
            var created = new TranslationString(key.Id, locale.Id, request.Value, actor);
            await _strings.AddAsync(created, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new UpsertTranslationStringResult(ToDto(created, locale.Code), Created: true);
        }

        if (request.ExpectedVersion is { } expected && expected != existing.Version)
        {
            throw new ConcurrencyException(existing.Version);
        }

        existing.Edit(request.Value, actor);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new UpsertTranslationStringResult(ToDto(existing, locale.Code), Created: false);
    }

    public async Task<TranslationStringDto?> ReviewAsync(
        Guid projectId,
        Guid keyId,
        Guid localeId,
        string action,
        string reviewedBy,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reviewedBy))
        {
            throw new ValidationException("A reviewer is required.");
        }

        var target = ToTargetState(action);

        var key = await _keys.GetAsync(projectId, keyId, cancellationToken);
        if (key is null)
        {
            return null;
        }

        var locale = await _locales.GetAsync(projectId, localeId, cancellationToken);
        if (locale is null)
        {
            return null;
        }

        var translationString = await _strings.GetAsync(keyId, localeId, cancellationToken);
        if (translationString is null)
        {
            return null;
        }

        translationString.ChangeReviewState(target, reviewedBy);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(translationString, locale.Code);
    }

    private static ReviewState ToTargetState(string action) => action?.Trim().ToLowerInvariant() switch
    {
        "submit" => ReviewState.NeedsReview,
        "approve" => ReviewState.Approved,
        "reject" => ReviewState.Draft,
        "reopen" => ReviewState.NeedsReview,
        _ => throw new ValidationException(
            $"Unknown review action '{action}'. Expected 'submit', 'approve', 'reject' or 'reopen'."),
    };

    private static TranslationStringDto ToDto(TranslationString s, string localeCode) => new(
        s.Id,
        s.TranslationKeyId,
        s.LocaleId,
        localeCode,
        s.Value,
        s.ReviewState.ToString(),
        s.UpdatedBy,
        s.Version,
        s.CreatedAt,
        s.UpdatedAt);
}

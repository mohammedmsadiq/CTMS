using CTMS.Application.Common;
using CTMS.Domain.Translations;

namespace CTMS.Application.Translations;

/// <summary>Persistence operations for the <see cref="TranslationString"/> aggregate.</summary>
public interface ITranslationStringRepository
{
    Task<IReadOnlyList<TranslationString>> ListByKeyAsync(Guid keyId, CancellationToken cancellationToken = default);

    Task<TranslationString?> GetAsync(Guid keyId, string languageCode, CancellationToken cancellationToken = default);

    /// <summary>Every string (any state, any language) for the given keys.</summary>
    Task<IReadOnlyList<TranslationString>> ListByKeyIdsAsync(
        IReadOnlyCollection<Guid> keyIds,
        CancellationToken cancellationToken = default);

    /// <summary>Every <see cref="ReviewState.Published"/> string (any language) for the given keys.</summary>
    Task<IReadOnlyList<TranslationString>> ListPublishedByKeyIdsAsync(
        IReadOnlyCollection<Guid> keyIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every <see cref="ReviewState.Approved"/> string for the given keys, optionally restricted to
    /// <paramref name="languageCode"/>. Used by bulk publish.
    /// </summary>
    Task<IReadOnlyList<TranslationString>> ListApprovedByKeyIdsAsync(
        IReadOnlyCollection<Guid> keyIds,
        string? languageCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of strings across the given keys, optionally filtered by <paramref name="state"/>,
    /// newest-updated first, together with the total match count.
    /// </summary>
    Task<PagedResult<TranslationString>> ListByKeysAndStateAsync(
        IReadOnlyCollection<Guid> keyIds,
        ReviewState? state,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    Task AddAsync(TranslationString translationString, CancellationToken cancellationToken = default);

    /// <summary>Persists an update to a stored string. Last write wins.</summary>
    Task UpdateAsync(TranslationString translationString, CancellationToken cancellationToken = default);
}

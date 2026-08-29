using CTMS.Application.Common;
using CTMS.Domain.Translations;

namespace CTMS.Application.Translations;

/// <summary>Persistence operations for the <see cref="TranslationString"/> aggregate.</summary>
public interface ITranslationStringRepository
{
    Task<IReadOnlyList<TranslationString>> ListByKeyAsync(Guid keyId, CancellationToken cancellationToken = default);

    Task<TranslationString?> GetAsync(Guid keyId, Guid localeId, CancellationToken cancellationToken = default);

    /// <summary>Every published string for a project's locale, keyed for bundle assembly.</summary>
    Task<IReadOnlyList<TranslationString>> ListByLocaleAndStateAsync(
        Guid localeId,
        ReviewState state,
        CancellationToken cancellationToken = default);

    Task AddAsync(TranslationString translationString, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists an update to a stored string, guarding on its current <see cref="TranslationString.Version"/>
    /// and advancing it. Throws <see cref="ConcurrencyException"/> (carrying the stored version) when the
    /// row was changed concurrently.
    /// </summary>
    Task UpdateAsync(TranslationString translationString, CancellationToken cancellationToken = default);
}

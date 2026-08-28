using CTMS.Domain.Translations;

namespace CTMS.Application.Translations;

/// <summary>Persistence operations for the <see cref="TranslationString"/> aggregate.</summary>
public interface ITranslationStringRepository
{
    Task<IReadOnlyList<TranslationString>> ListByKeyAsync(Guid keyId, CancellationToken cancellationToken = default);

    Task<TranslationString?> GetAsync(Guid keyId, Guid localeId, CancellationToken cancellationToken = default);

    Task AddAsync(TranslationString translationString, CancellationToken cancellationToken = default);
}

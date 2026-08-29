using CTMS.Domain.Translations;

namespace CTMS.Application.Translations;

/// <summary>Persistence operations for the <see cref="TranslationKey"/> aggregate.</summary>
public interface ITranslationKeyRepository
{
    Task<IReadOnlyList<TranslationKey>> ListByProjectAsync(
        Guid projectId,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    Task<int> CountByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<TranslationKey?> GetAsync(Guid projectId, Guid keyId, CancellationToken cancellationToken = default);

    Task<bool> KeyNameExistsAsync(Guid projectId, string keyName, CancellationToken cancellationToken = default);

    Task AddAsync(TranslationKey key, CancellationToken cancellationToken = default);

    /// <summary>Persists changes to an already-stored key.</summary>
    Task UpdateAsync(TranslationKey key, CancellationToken cancellationToken = default);

    /// <summary>Removes the key and every translation string that belongs to it.</summary>
    Task RemoveAsync(TranslationKey key, CancellationToken cancellationToken = default);
}

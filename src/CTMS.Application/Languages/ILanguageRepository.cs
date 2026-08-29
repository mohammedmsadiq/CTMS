using CTMS.Domain.Languages;

namespace CTMS.Application.Languages;

/// <summary>Persistence operations for the global <see cref="Language"/> aggregate.</summary>
public interface ILanguageRepository
{
    Task<IReadOnlyList<Language>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);

    /// <summary>Every language, active or not, keyed by <see cref="Language.Code"/>.</summary>
    Task<IReadOnlyList<Language>> ListAllAsync(CancellationToken cancellationToken = default);

    Task<Language?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);

    Task AddAsync(Language language, CancellationToken cancellationToken = default);

    Task UpdateAsync(Language language, CancellationToken cancellationToken = default);
}

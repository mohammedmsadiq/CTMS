using CTMS.Domain.Locales;

namespace CTMS.Application.Locales;

/// <summary>Persistence operations for the <see cref="Locale"/> aggregate.</summary>
public interface ILocaleRepository
{
    Task<IReadOnlyList<Locale>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<Locale?> GetAsync(Guid projectId, Guid localeId, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(Guid projectId, string code, CancellationToken cancellationToken = default);

    Task AddAsync(Locale locale, CancellationToken cancellationToken = default);

    /// <summary>Removes the locale and every translation string that targets it.</summary>
    Task RemoveAsync(Locale locale, CancellationToken cancellationToken = default);
}

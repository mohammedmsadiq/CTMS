using CTMS.Domain.ApiKeys;

namespace CTMS.Application.ApiKeys;

/// <summary>Persistence for the <see cref="ApiKey"/> aggregate (collection <c>apiKeys</c>).</summary>
public interface IApiKeyRepository
{
    /// <summary>The key whose <see cref="ApiKey.Hash"/> matches, or <c>null</c>. Backs authentication.</summary>
    Task<ApiKey?> FindByHashAsync(string hash, CancellationToken cancellationToken = default);

    Task<ApiKey?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every key, newest first. No secrets are ever returned — the entity carries only the hash.</summary>
    Task<IReadOnlyList<ApiKey>> ListAsync(CancellationToken cancellationToken = default);

    Task InsertAsync(ApiKey apiKey, CancellationToken cancellationToken = default);

    /// <summary>Hard-deletes the key. Returns <c>true</c> when a row was removed.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Stamps <see cref="ApiKey.LastUsedAt"/>. Called fire-and-forget; failures are swallowed.</summary>
    Task TouchAsync(Guid id, DateTime whenUtc, CancellationToken cancellationToken = default);
}

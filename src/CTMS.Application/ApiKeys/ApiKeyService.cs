using CTMS.Application.Common;
using CTMS.Domain.ApiKeys;

namespace CTMS.Application.ApiKeys;

/// <summary>Use-case orchestration for machine API keys (mint / list / revoke).</summary>
public sealed class ApiKeyService
{
    private readonly IApiKeyRepository _apiKeys;

    public ApiKeyService(IApiKeyRepository apiKeys) => _apiKeys = apiKeys;

    /// <summary>
    /// Mints a new key. The raw value is returned <b>once</b> in <see cref="CreatedApiKeyDto.Key"/>
    /// and never stored; only its hash and display prefix are persisted.
    /// </summary>
    public async Task<CreatedApiKeyDto> CreateAsync(
        CreateApiKeyRequest request,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("An API key name is required.");
        }

        var actor = string.IsNullOrWhiteSpace(createdBy) ? "system" : createdBy.Trim();

        var rawKey = ApiKeySecret.NewRawKey();
        var apiKey = new ApiKey(request.Name, ApiKeySecret.Hash(rawKey), ApiKeySecret.PrefixOf(rawKey), actor);

        await _apiKeys.InsertAsync(apiKey, cancellationToken);

        return new CreatedApiKeyDto(
            apiKey.Id,
            apiKey.Name,
            apiKey.Prefix,
            apiKey.CreatedBy,
            apiKey.Active,
            apiKey.CreatedAt,
            rawKey);
    }

    public async Task<IReadOnlyList<ApiKeyDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var keys = await _apiKeys.ListAsync(cancellationToken);
        return keys.Select(ToDto).ToList();
    }

    /// <summary>Hard-deletes a key. <c>false</c> when no key with that id exists.</summary>
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => _apiKeys.DeleteAsync(id, cancellationToken);

    private static ApiKeyDto ToDto(ApiKey key) => new(
        key.Id,
        key.Name,
        key.Prefix,
        key.CreatedBy,
        key.Active,
        key.LastUsedAt,
        key.CreatedAt);
}

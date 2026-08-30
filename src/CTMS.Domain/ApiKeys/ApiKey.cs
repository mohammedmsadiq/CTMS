using CTMS.Domain.Common;

namespace CTMS.Domain.ApiKeys;

/// <summary>
/// A long-lived credential a machine client (a CI job, a server-side site) presents in the
/// <c>X-Api-Key</c> header to make <em>authenticated read-only</em> calls without an Entra token.
/// The raw key is shown once at creation and never stored; only its <see cref="Hash"/> (a
/// Base64 SHA-256 digest) and a short display <see cref="Prefix"/> are persisted.
/// </summary>
public sealed class ApiKey : Entity
{
    private ApiKey()
    {
        // Materialization constructor for the persistence layer.
    }

    public ApiKey(string name, string hash, string prefix, string createdBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdBy);

        Name = name.Trim();
        Hash = hash;
        Prefix = prefix;
        CreatedBy = createdBy.Trim();
        Active = true;
    }

    /// <summary>Human-readable label, shown wherever the key is listed. Also the principal name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Base64 of the SHA-256 of the raw key. The only form of the secret CTMS keeps.</summary>
    public string Hash { get; private set; } = string.Empty;

    /// <summary>First 8 characters of the raw key (<c>ctms_...</c>) — for display only, not a secret.</summary>
    public string Prefix { get; private set; } = string.Empty;

    /// <summary>Actor who minted the key (token identity, or the request-body value when anonymous).</summary>
    public string CreatedBy { get; private set; } = string.Empty;

    /// <summary>An inactive key never authenticates.</summary>
    public bool Active { get; private set; }

    /// <summary>When the key last successfully authenticated a request. Best-effort, may lag.</summary>
    public DateTime? LastUsedAt { get; private set; }

    public void Deactivate() => Active = false;

    /// <summary>Records a successful authentication. Called fire-and-forget off the request path.</summary>
    public void MarkUsed(DateTime whenUtc) => LastUsedAt = whenUtc;
}

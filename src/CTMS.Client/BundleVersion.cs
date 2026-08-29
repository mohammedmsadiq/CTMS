using System;

namespace CTMS.Client;

/// <summary>
/// One entry from a bundle's version history (<c>GET .../bundles/{locale}/versions</c>), without
/// the entries payload. Use <see cref="Version"/> with
/// <see cref="ICtmsClient.GetBundleAsync(string, int, System.Threading.CancellationToken)"/> to pin.
/// </summary>
public sealed class BundleVersion
{
    internal BundleVersion(int version, string etag, DateTimeOffset createdAt, string createdBy, int entryCount)
    {
        Version = version;
        Etag = etag;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        EntryCount = entryCount;
    }

    /// <summary>Publish number, ascending in the history list.</summary>
    public int Version { get; }

    /// <summary>Raw lowercase-hex SHA-256 content hash (unquoted).</summary>
    public string Etag { get; }

    /// <summary>Server publish timestamp.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Actor that published this version.</summary>
    public string CreatedBy { get; }

    /// <summary>Number of key/value pairs in this version.</summary>
    public int EntryCount { get; }
}

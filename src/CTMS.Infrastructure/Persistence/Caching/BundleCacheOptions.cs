namespace CTMS.Infrastructure.Persistence.Caching;

/// <summary>Bound from the <c>Cache</c> configuration section.</summary>
public sealed class BundleCacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>Default time-to-live for a cached latest bundle, in minutes.</summary>
    public const int DefaultTtlMinutes = 60;

    /// <summary>
    /// Time-to-live for a cached latest bundle, in minutes (config key
    /// <c>Cache:BundleTtlMinutes</c>). A value &lt;= 0 falls back to
    /// <see cref="DefaultTtlMinutes"/>.
    /// </summary>
    public int BundleTtlMinutes { get; set; } = DefaultTtlMinutes;
}

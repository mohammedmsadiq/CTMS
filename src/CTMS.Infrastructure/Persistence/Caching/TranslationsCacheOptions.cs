namespace CTMS.Infrastructure.Persistence.Caching;

/// <summary>Bound from the <c>Cache</c> configuration section.</summary>
public sealed class TranslationsCacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>Default time-to-live for a cached assembled translation map, in minutes.</summary>
    public const int DefaultTtlMinutes = 60;

    /// <summary>
    /// Time-to-live for a cached translation map, in minutes (config key
    /// <c>Cache:TranslationsTtlMinutes</c>). A value &lt;= 0 falls back to
    /// <see cref="DefaultTtlMinutes"/>.
    /// </summary>
    public int TranslationsTtlMinutes { get; set; } = DefaultTtlMinutes;
}

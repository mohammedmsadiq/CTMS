namespace CTMS.Application.Translations;

/// <summary>
/// Derives a default <see cref="Domain.Translations.TranslationKey.Category"/> from a key name
/// when the caller does not supply one. The rule: take the segment before the first <c>.</c> and
/// title-case it (<c>course.start</c> → <c>Course</c>, <c>nav.home.link</c> → <c>Nav</c>); a key
/// with no <c>.</c> — or nothing usable before the first one — falls back to <c>General</c>.
/// </summary>
public static class CategorySuggestion
{
    /// <summary>The category used when a key name carries no usable dotted prefix.</summary>
    public const string Fallback = "General";

    /// <summary>
    /// Returns the derived category for <paramref name="keyName"/>. Never returns null or blank —
    /// the domain always stores a non-blank category.
    /// </summary>
    public static string FromKeyName(string? keyName)
    {
        var trimmed = keyName?.Trim() ?? string.Empty;

        var dot = trimmed.IndexOf('.');
        if (dot < 0)
        {
            return Fallback;
        }

        var prefix = trimmed[..dot].Trim();
        if (prefix.Length == 0)
        {
            return Fallback;
        }

        return char.ToUpperInvariant(prefix[0]) + prefix[1..].ToLowerInvariant();
    }
}

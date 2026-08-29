using System;
using System.Collections.Generic;

namespace CTMS.Client.Internal;

/// <summary>Builds the ordered, de-duplicated locale fallback chain for key resolution.</summary>
internal static class LocaleChain
{
    /// <summary>
    /// Yields, in priority order: the requested locale and each parent (<c>fr-CA</c> → <c>fr</c>),
    /// then every explicit fallback locale expanded the same way, then the default locale expanded
    /// the same way. Comparisons are case-insensitive; each locale is emitted at most once.
    /// </summary>
    public static IEnumerable<string> Build(string locale, IReadOnlyList<string> fallbackLocales, string? defaultLocale)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in Expand(locale))
        {
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }

        foreach (var fallback in fallbackLocales)
        {
            foreach (var candidate in Expand(fallback))
            {
                if (seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(defaultLocale))
        {
            foreach (var candidate in Expand(defaultLocale!))
            {
                if (seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    /// <summary><c>zh-Hant-TW</c> → <c>zh-Hant-TW</c>, <c>zh-Hant</c>, <c>zh</c>.</summary>
    public static IEnumerable<string> Expand(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            yield break;
        }

        var parts = locale!.Trim().Split('-');
        for (var take = parts.Length; take >= 1; take--)
        {
            var segment = string.Join("-", parts, 0, take);
            if (segment.Length > 0)
            {
                yield return segment;
            }
        }
    }
}

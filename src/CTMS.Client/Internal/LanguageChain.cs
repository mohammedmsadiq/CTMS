using System;
using System.Collections.Generic;

namespace CTMS.Client.Internal;

/// <summary>
/// Builds the ordered, de-duplicated client-side language fallback chain for key resolution.
/// The API already fills missing keys server-side from each language's <c>FallbackCode</c> chain;
/// this chain is a secondary safety net across the languages a caller has actually loaded.
/// </summary>
internal static class LanguageChain
{
    /// <summary>
    /// Yields, in priority order and each at most once (case-insensitive): the requested language,
    /// then every explicit extra fallback language, then the configured default language.
    /// </summary>
    public static IEnumerable<string> Build(string language, IReadOnlyList<string> extraFallbackLanguages, string? defaultLanguage)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in Candidates(language, extraFallbackLanguages, defaultLanguage))
        {
            if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate.Trim()))
            {
                yield return candidate.Trim();
            }
        }
    }

    private static IEnumerable<string> Candidates(string language, IReadOnlyList<string> extras, string? defaultLanguage)
    {
        yield return language;

        foreach (var extra in extras)
        {
            yield return extra;
        }

        if (!string.IsNullOrWhiteSpace(defaultLanguage))
        {
            yield return defaultLanguage!;
        }
    }
}

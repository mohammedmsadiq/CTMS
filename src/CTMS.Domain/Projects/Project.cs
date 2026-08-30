using CTMS.Domain.Common;

namespace CTMS.Domain.Projects;

/// <summary>
/// A translatable application whose strings CTMS manages. The <see cref="Slug"/> doubles as the
/// application <em>code</em> used on the client delivery routes. A <see cref="IsCommon"/>
/// application (e.g. <c>common</c>) contributes its published translations to every other
/// application's bundle.
/// </summary>
public sealed class Project : Entity
{
    private Project()
    {
        // Materialization constructor for the persistence layer.
    }

    public Project(
        string name,
        string slug,
        string baseLanguageCode,
        string? description = null,
        bool isCommon = false,
        bool active = true)
    {
        Rename(name);
        SetSlug(slug);
        SetBaseLanguageCode(baseLanguageCode);
        UpdateDescription(description);
        IsCommon = isCommon;
        Active = active;
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>URL-safe unique identifier and the application <em>code</em>, e.g. <c>icoach</c>.</summary>
    public string Slug { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>BCP-47 code of the language source strings are authored in.</summary>
    public string BaseLanguageCode { get; private set; } = string.Empty;

    /// <summary>A common project's published translations merge into every project's bundle.</summary>
    public bool IsCommon { get; private set; }

    /// <summary>Inactive applications are hidden from delivery.</summary>
    public bool Active { get; private set; }

    /// <summary>BCP-47 codes of the languages enabled for this application. Ordinal, de-duplicated.</summary>
    public IReadOnlyList<string> EnabledLanguageCodes { get; private set; } = [];

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public void SetSlug(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        Slug = slug.Trim().ToLowerInvariant();
    }

    public void SetBaseLanguageCode(string baseLanguageCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseLanguageCode);
        BaseLanguageCode = baseLanguageCode.Trim();
    }

    public void UpdateDescription(string? description)
        => Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    public void SetCommon(bool isCommon) => IsCommon = isCommon;

    public void SetActive(bool active) => Active = active;

    /// <summary>
    /// Adds <paramref name="languageCode"/> to the enabled set (no-op if already present). Callers
    /// are responsible for checking the language exists and is active.
    /// </summary>
    public void EnableLanguage(string languageCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageCode);
        var code = languageCode.Trim();

        if (EnabledLanguageCodes.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        EnabledLanguageCodes = [.. EnabledLanguageCodes, code];
    }

    /// <summary>Removes <paramref name="languageCode"/> from the enabled set (no-op if absent).</summary>
    public void DisableLanguage(string languageCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageCode);
        var code = languageCode.Trim();

        EnabledLanguageCodes =
        [
            .. EnabledLanguageCodes.Where(c => !string.Equals(c, code, StringComparison.OrdinalIgnoreCase)),
        ];
    }

    /// <summary>Replaces the enabled set wholesale, trimming and de-duplicating (ordinal-ignore-case).</summary>
    public void SetEnabledLanguages(IEnumerable<string> languageCodes)
    {
        ArgumentNullException.ThrowIfNull(languageCodes);

        var result = new List<string>();
        foreach (var raw in languageCodes)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var code = raw.Trim();
            if (!result.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(code);
            }
        }

        EnabledLanguageCodes = result;
    }
}

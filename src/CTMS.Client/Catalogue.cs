using System;
using System.Collections.Generic;

namespace CTMS.Client;

/// <summary>
/// One entry from <c>GET /api/languages</c> — a global language in the CTMS catalogue. Useful for
/// building a language picker.
/// </summary>
public sealed class LanguageInfo
{
    internal LanguageInfo(
        string code,
        string name,
        string? fallbackCode,
        bool isRtl,
        bool active,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Code = code;
        Name = name;
        FallbackCode = fallbackCode;
        IsRtl = isRtl;
        Active = active;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>BCP-47 code, e.g. <c>fr-FR</c>.</summary>
    public string Code { get; }

    /// <summary>Human-readable name.</summary>
    public string Name { get; }

    /// <summary>The language the server falls back to for missing keys (chain-walked), if any.</summary>
    public string? FallbackCode { get; }

    /// <summary>Right-to-left script.</summary>
    public bool IsRtl { get; }

    /// <summary>Whether the language is active.</summary>
    public bool Active { get; }

    /// <summary>Server creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Server last-update timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; }
}

/// <summary>
/// One entry from <c>GET /api/applications</c> — an application (the <c>Project</c> aggregate),
/// keyed by its <see cref="Code"/> (the slug used on client routes).
/// </summary>
public sealed class ApplicationInfo
{
    internal ApplicationInfo(
        string code,
        string name,
        string? description,
        bool isShared,
        bool active,
        string baseLanguageCode,
        IReadOnlyList<string> enabledLanguageCodes,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Code = code;
        Name = name;
        Description = description;
        IsShared = isShared;
        Active = active;
        BaseLanguageCode = baseLanguageCode;
        EnabledLanguageCodes = enabledLanguageCodes;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>Application code / slug, e.g. <c>icoach</c>.</summary>
    public string Code { get; }

    /// <summary>Human-readable name.</summary>
    public string Name { get; }

    /// <summary>Optional description.</summary>
    public string? Description { get; }

    /// <summary>A shared application whose published translations merge into every application's set.</summary>
    public bool IsShared { get; }

    /// <summary>Whether the application is active.</summary>
    public bool Active { get; }

    /// <summary>The application's base language code.</summary>
    public string BaseLanguageCode { get; }

    /// <summary>Language codes enabled for this application.</summary>
    public IReadOnlyList<string> EnabledLanguageCodes { get; }

    /// <summary>Server creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Server last-update timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; }
}

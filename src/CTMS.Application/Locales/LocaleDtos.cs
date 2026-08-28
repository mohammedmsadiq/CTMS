namespace CTMS.Application.Locales;

/// <summary>Read model returned by the locales API.</summary>
public sealed record LocaleDto(
    Guid Id,
    Guid ProjectId,
    string Code,
    string DisplayName,
    bool IsRtl,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Payload for enabling a locale on a project.</summary>
public sealed record CreateLocaleRequest(string Code, string DisplayName, bool IsRtl = false);

/// <summary>Partial update for a locale; omitted members are left unchanged.</summary>
public sealed record UpdateLocaleRequest(string? DisplayName = null, bool? IsRtl = null);

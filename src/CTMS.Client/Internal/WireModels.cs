using System;
using System.Collections.Generic;

namespace CTMS.Client.Internal;

/// <summary>Wire shape of <c>PublishedTranslationsResponse</c> from the CTMS API.</summary>
internal sealed class TranslationsWire
{
    public string Application { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public Dictionary<string, string> Translations { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Wire shape of <c>LanguageDto</c> from <c>GET /api/languages</c>.</summary>
internal sealed class LanguageWire
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? FallbackCode { get; set; }

    public bool IsRtl { get; set; }

    public bool Active { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Wire shape of <c>ApplicationDto</c> from <c>GET /api/applications</c>.</summary>
internal sealed class ApplicationWire
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsShared { get; set; }

    public bool Active { get; set; }

    public string BaseLanguageCode { get; set; } = string.Empty;

    public List<string> EnabledLanguageCodes { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Subset of an RFC 7807 <c>application/problem+json</c> body.</summary>
internal sealed class ProblemWire
{
    public string? Title { get; set; }

    public string? Detail { get; set; }

    public int? Status { get; set; }
}

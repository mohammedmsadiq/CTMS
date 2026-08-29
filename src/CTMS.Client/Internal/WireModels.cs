using System;
using System.Collections.Generic;

namespace CTMS.Client.Internal;

/// <summary>Wire shape of <c>TranslationBundleDto</c> from the CTMS API.</summary>
internal sealed class BundleWire
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public string LocaleCode { get; set; } = string.Empty;

    public int Version { get; set; }

    public Dictionary<string, string> Entries { get; set; } = new(StringComparer.Ordinal);

    public string ETag { get; set; } = string.Empty;

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Wire shape of <c>BundleVersionDto</c> from the CTMS API.</summary>
internal sealed class BundleVersionWire
{
    public int Version { get; set; }

    public string ETag { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public int EntryCount { get; set; }
}

/// <summary>Subset of an RFC 7807 <c>application/problem+json</c> body.</summary>
internal sealed class ProblemWire
{
    public string? Title { get; set; }

    public string? Detail { get; set; }

    public int? Status { get; set; }
}

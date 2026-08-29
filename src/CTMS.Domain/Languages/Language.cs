using CTMS.Domain.Common;

namespace CTMS.Domain.Languages;

/// <summary>
/// A language CTMS can translate into. Global — shared by every application — and keyed by its
/// BCP-47 <see cref="Code"/> (e.g. <c>en-GB</c>, <c>fr-FR</c>). A language may name a
/// <see cref="FallbackCode"/>: when an application has no published value for a key in this
/// language, the assembler walks the fallback chain (<c>fr-CA</c> → <c>fr-FR</c> → <c>en-GB</c>).
/// </summary>
public sealed class Language : Entity
{
    private Language()
    {
        // Materialization constructor for the persistence layer.
    }

    public Language(string code, string name, string? fallbackCode = null, bool isRtl = false, bool active = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Code = code.Trim();
        Name = name.Trim();
        SetFallbackCode(fallbackCode);
        IsRtl = isRtl;
        Active = active;
    }

    /// <summary>BCP-47 language tag, unique across CTMS. Trimmed; casing preserved.</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>Another language's <see cref="Code"/> to fall back to, or <c>null</c>.</summary>
    public string? FallbackCode { get; private set; }

    public bool IsRtl { get; private set; }

    /// <summary>Inactive languages are hidden from delivery and rejected by the assembler.</summary>
    public bool Active { get; private set; }

    public void SetName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public void SetFallbackCode(string? fallbackCode)
    {
        var trimmed = string.IsNullOrWhiteSpace(fallbackCode) ? null : fallbackCode.Trim();

        if (trimmed is not null && string.Equals(trimmed, Code, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A language cannot fall back to itself.", nameof(fallbackCode));
        }

        FallbackCode = trimmed;
    }

    public void SetRightToLeft(bool isRtl) => IsRtl = isRtl;

    public void SetActive(bool active) => Active = active;
}

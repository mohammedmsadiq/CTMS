using CTMS.Domain.Common;

namespace CTMS.Domain.Projects;

/// <summary>A translatable product or application whose strings CTMS manages.</summary>
public sealed class Project : Entity
{
    private Project()
    {
        // Materialization constructor for the persistence layer.
    }

    public Project(string name, string slug, string baseLocaleCode, string? description = null)
    {
        Rename(name);
        SetSlug(slug);
        SetBaseLocaleCode(baseLocaleCode);
        UpdateDescription(description);
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>URL-safe unique identifier, e.g. <c>acme-web</c>.</summary>
    public string Slug { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>BCP-47 code of the locale that source strings are authored in.</summary>
    public string BaseLocaleCode { get; private set; } = string.Empty;

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

    public void SetBaseLocaleCode(string baseLocaleCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseLocaleCode);
        BaseLocaleCode = baseLocaleCode.Trim();
    }

    public void UpdateDescription(string? description)
        => Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}

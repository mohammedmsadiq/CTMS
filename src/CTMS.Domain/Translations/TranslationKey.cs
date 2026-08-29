using CTMS.Domain.Common;

namespace CTMS.Domain.Translations;

/// <summary>A named slot for a piece of translatable text within an application.</summary>
public sealed class TranslationKey : Entity
{
    private TranslationKey()
    {
        // Materialization constructor for the persistence layer.
    }

    public TranslationKey(
        Guid projectId,
        string keyName,
        string category,
        string createdBy,
        string? description = null)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("A translation key must belong to an application.", nameof(projectId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdBy);

        ProjectId = projectId;
        KeyName = keyName.Trim();
        Category = category.Trim();
        CreatedBy = createdBy.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Active = true;
    }

    public Guid ProjectId { get; private set; }

    /// <summary>Dotted path such as <c>checkout.button.submit</c>. Unique within an application.</summary>
    public string KeyName { get; private set; } = string.Empty;

    /// <summary>Grouping label such as <c>Common</c>, <c>Navigation</c>, <c>Course</c>.</summary>
    public string Category { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>Inactive keys are excluded from delivery and coverage.</summary>
    public bool Active { get; private set; }

    /// <summary>Actor who first created the key.</summary>
    public string CreatedBy { get; private set; } = string.Empty;

    public void Describe(string? description)
        => Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    public void SetCategory(string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        Category = category.Trim();
    }

    public void SetActive(bool active) => Active = active;
}

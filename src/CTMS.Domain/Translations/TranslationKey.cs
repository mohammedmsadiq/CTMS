using CTMS.Domain.Common;

namespace CTMS.Domain.Translations;

/// <summary>A named slot for a piece of translatable text within a project.</summary>
public sealed class TranslationKey : Entity
{
    private TranslationKey()
    {
        // Materialization constructor for the persistence layer.
    }

    public TranslationKey(Guid projectId, string keyName, string? description = null)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("A translation key must belong to a project.", nameof(projectId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);

        ProjectId = projectId;
        KeyName = keyName.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    public Guid ProjectId { get; private set; }

    /// <summary>Dotted path such as <c>checkout.button.submit</c>. Unique within a project.</summary>
    public string KeyName { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public void Describe(string? description)
        => Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}

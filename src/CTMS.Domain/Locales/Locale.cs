using CTMS.Domain.Common;

namespace CTMS.Domain.Locales;

/// <summary>A locale enabled for a specific project.</summary>
public sealed class Locale : Entity
{
    private Locale()
    {
        // Materialization constructor for the persistence layer.
    }

    public Locale(Guid projectId, string code, string displayName, bool isRtl = false)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("A locale must belong to a project.", nameof(projectId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        ProjectId = projectId;
        Code = code.Trim();
        DisplayName = displayName.Trim();
        IsRtl = isRtl;
    }

    public Guid ProjectId { get; private set; }

    /// <summary>BCP-47 language tag, e.g. <c>en-US</c> or <c>ar</c>.</summary>
    public string Code { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public bool IsRtl { get; private set; }

    public void SetDisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName.Trim();
    }

    public void SetRightToLeft(bool isRtl) => IsRtl = isRtl;
}

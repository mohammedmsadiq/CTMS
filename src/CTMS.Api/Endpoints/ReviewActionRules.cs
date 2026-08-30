namespace CTMS.Api.Endpoints;

/// <summary>
/// Which review actions a translator may perform. Per the spec's authorisation matrix a
/// translator can only <c>submit</c> their own work for review; approve/reject/reopen/
/// publish/archive/unarchive require <c>CanReview</c>.
/// </summary>
internal static class ReviewActionRules
{
    public static bool IsSubmit(string? action) =>
        string.Equals(action?.Trim(), "submit", System.StringComparison.OrdinalIgnoreCase);
}

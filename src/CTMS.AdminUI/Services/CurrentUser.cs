namespace CTMS.AdminUI.Services;

/// <summary>
/// Placeholder for the signed-in principal. Real authentication is WS7 — until then this
/// hands out a fixed display name that pages pass as <c>updatedBy</c> / <c>reviewedBy</c>.
/// </summary>
// TODO: WS7 auth — replace with a claims-backed accessor (AuthenticationStateProvider).
public sealed class CurrentUser
{
    public string DisplayName => "admin (stub)";

    public bool IsAuthenticated => false;
}

using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace CTMS.AdminUI.Services;

/// <summary>
/// Claims-backed view of the signed-in principal, kept in the same shape earlier pages already
/// consume (<see cref="DisplayName"/> / <see cref="IsAuthenticated"/>). Pages pass
/// <see cref="DisplayName"/> as <c>updatedBy</c> / <c>reviewedBy</c>; the API overrides those
/// from the token anyway, but the UI stays honest. Adds <see cref="Roles"/> /
/// <see cref="IsInRole"/> for role-gating helpers.
/// </summary>
public sealed class CurrentUser : IDisposable
{
    private const string Anonymous = "anonymous";

    private readonly AuthenticationStateProvider _authState;
    private ClaimsPrincipal _principal = new(new ClaimsIdentity());

    public CurrentUser(AuthenticationStateProvider authState)
    {
        _authState = authState;
        _authState.AuthenticationStateChanged += OnAuthenticationStateChanged;

        // Best-effort synchronous seed: on an established Blazor Server circuit the task is
        // already completed. EnsureLoadedAsync() is the reliable path for callers that can await.
        var pending = _authState.GetAuthenticationStateAsync();
        if (pending.IsCompletedSuccessfully)
        {
            _principal = pending.Result.User;
        }
    }

    /// <summary>Display name: <c>name</c> claim, then <c>preferred_username</c>, then identity name.</summary>
    public string DisplayName => IsAuthenticated
        ? _principal.FindFirst("name")?.Value
          ?? _principal.FindFirst("preferred_username")?.Value
          ?? _principal.Identity?.Name
          ?? "unknown"
        : Anonymous;

    public bool IsAuthenticated => _principal.Identity?.IsAuthenticated == true;

    public IReadOnlyCollection<string> Roles => _principal
        .FindAll(ClaimTypes.Role)
        .Concat(_principal.FindAll("roles"))
        .Select(c => c.Value)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    public bool IsInRole(string role) => _principal.IsInRole(role) || Roles.Contains(role);

    /// <summary>Refresh from the current authentication state. Call from <c>OnInitializedAsync</c>.</summary>
    public async Task EnsureLoadedAsync()
        => _principal = (await _authState.GetAuthenticationStateAsync()).User;

    private void OnAuthenticationStateChanged(Task<AuthenticationState> task)
        => _ = UpdateAsync(task);

    private async Task UpdateAsync(Task<AuthenticationState> task)
    {
        try
        {
            _principal = (await task).User;
        }
        catch
        {
            _principal = new ClaimsPrincipal(new ClaimsIdentity());
        }
    }

    public void Dispose()
        => _authState.AuthenticationStateChanged -= OnAuthenticationStateChanged;
}

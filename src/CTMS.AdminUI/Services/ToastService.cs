namespace CTMS.AdminUI.Services;

/// <summary>Severity of a transient toast notification.</summary>
public enum ToastLevel
{
    Success,
    Info,
    Warning,
    Error,
}

/// <summary>One queued toast.</summary>
public sealed record Toast(Guid Id, ToastLevel Level, string Message);

/// <summary>
/// Scoped, in-circuit toast queue. Components raise <see cref="Show"/> after a successful
/// mutation; <c>ToastHost</c> renders the queue and calls <see cref="Dismiss"/>. Auto-expiry is
/// driven by the host (a component can own a timer safely), not this service.
/// </summary>
public sealed class ToastService
{
    private readonly List<Toast> _toasts = new();

    public IReadOnlyList<Toast> Current => _toasts;

    public event Action? OnChange;

    public void Show(ToastLevel level, string message)
    {
        _toasts.Add(new Toast(Guid.NewGuid(), level, message));
        OnChange?.Invoke();
    }

    public void ShowSuccess(string message) => Show(ToastLevel.Success, message);

    public void ShowError(string message) => Show(ToastLevel.Error, message);

    public void Dismiss(Guid id)
    {
        if (_toasts.RemoveAll(t => t.Id == id) > 0)
        {
            OnChange?.Invoke();
        }
    }
}

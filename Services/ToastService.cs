namespace JobTracker.Services;

public enum ToastLevel { Success, Info, Error }

public record Toast(Guid Id, string Message, ToastLevel Level);

/// <summary>Short-lived notifications shown in the corner of the screen. One instance per circuit.</summary>
public class ToastService
{
    private readonly List<Toast> _toasts = [];

    public IReadOnlyList<Toast> Toasts => _toasts;

    public event Action? Changed;

    public void Success(string message) => Show(message, ToastLevel.Success);

    public void Info(string message) => Show(message, ToastLevel.Info);

    public void Error(string message) => Show(message, ToastLevel.Error);

    public void Dismiss(Guid id)
    {
        if (_toasts.RemoveAll(t => t.Id == id) > 0)
        {
            Changed?.Invoke();
        }
    }

    private void Show(string message, ToastLevel level)
    {
        var toast = new Toast(Guid.NewGuid(), message, level);
        _toasts.Add(toast);
        Changed?.Invoke();
        _ = DismissLaterAsync(toast.Id);
    }

    private async Task DismissLaterAsync(Guid id)
    {
        await Task.Delay(TimeSpan.FromSeconds(4));
        Dismiss(id);
    }
}

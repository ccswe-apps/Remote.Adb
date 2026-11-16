namespace Remote.Adb.Desktop.Common;

/// <summary>
/// A dialog view model that asks its host to close with a boolean result, so the view model never touches the
/// <c>Window</c>. Shown via <see cref="DialogHost"/>, which also tells it when the dialog opens and closes — the hooks
/// for starting and stopping background work (polling) for exactly the dialog's lifetime. A dialog view model is a
/// transient resolved from the root container, so it must not rely on <see cref="IDisposable"/> for that.
/// </summary>
public interface IDialogViewModel
{
    /// <summary>Raised when the dialog wants to close; the argument is the dialog's result.</summary>
    event Action<bool>? CloseRequested;

    /// <summary>Called after the dialog closes, however it was closed (including the window's close button).</summary>
    void OnClosed()
    {
    }

    /// <summary>Called just before the dialog is shown.</summary>
    void OnOpened()
    {
    }
}

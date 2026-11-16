using CommunityToolkit.Mvvm.ComponentModel;
using Remote.Adb.Core.Common;
using Remote.Adb.Desktop.Common;

namespace Remote.Adb.Desktop.Devices.Wireless;

/// <summary>
/// Base for the wireless dialog's forms (pair with a code, pair by QR, connect to an address): one attempt at a time,
/// a busy flag for the progress indicator, and an inline error line — the try/busy/error shape each form shares.
/// </summary>
public abstract partial class FormViewModelBase : ViewModelBase
{
    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// Runs <paramref name="attempt"/> with <see cref="IsBusy"/> set and the previous error cleared. The attempt returns
    /// <see langword="null"/> on success or the error to show; a tool that couldn't be launched shows its message.
    /// Returns whether the attempt succeeded.
    /// </summary>
    protected async Task<bool> RunGuardedAsync(Func<Task<string?>> attempt)
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            ErrorMessage = await attempt();
        }
        catch (ProcessLaunchException exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }

        return ErrorMessage is null;
    }
}

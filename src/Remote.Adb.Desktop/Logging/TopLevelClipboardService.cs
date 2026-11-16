using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace Remote.Adb.Desktop.Logging;

/// <summary>
/// <see cref="IClipboardService"/> over the main window's clipboard. The window is looked up at call time (not
/// injected) because the main window's own view model graph depends on this service.
/// </summary>
public sealed class TopLevelClipboardService : IClipboardService
{
    public async Task SetTextAsync(string text)
    {
        var clipboard = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        await clipboard.SetTextAsync(text);
    }
}

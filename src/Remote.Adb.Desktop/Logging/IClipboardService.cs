namespace Remote.Adb.Desktop.Logging;

/// <summary>Puts text on the system clipboard. Keeps view models free of Avalonia's <c>TopLevel</c>.</summary>
public interface IClipboardService
{
    /// <summary>Replaces the clipboard's contents with <paramref name="text"/>.</summary>
    Task SetTextAsync(string text);
}

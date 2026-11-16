using CommunityToolkit.Mvvm.ComponentModel;

namespace Remote.Adb.Desktop.Settings;

/// <summary>
/// One folder-override setting (SDK path, AVD home, JDK path): the typed value, its validation error, and a readout of
/// the path actually in use. A typed value is normalized the same way the settings store does (blank → cleared,
/// otherwise trimmed) and must name an existing folder; only a valid value is persisted, so the validated value and the
/// stored value always match.
/// </summary>
public sealed class PathOverrideField : ObservableObject
{
    private string? _error;
    private readonly Action<string?> _persist;
    private readonly Func<string> _status;
    private string? _value;

    /// <param name="label">The field label.</param>
    /// <param name="placeholder">The hint shown while the field is blank.</param>
    /// <param name="value">The persisted override, or <see langword="null"/> for none.</param>
    /// <param name="persist">Stores a validated (normalized) override.</param>
    /// <param name="status">Describes the path in use; re-read after each persisted change.</param>
    public PathOverrideField(string label, string placeholder, string? value, Action<string?> persist, Func<string> status)
    {
        Label = label;
        Placeholder = placeholder;
        _value = value;
        _persist = persist;
        _status = status;
    }

    /// <summary>Why the typed value wasn't saved, or <see langword="null"/> when it was.</summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public string Label { get; }

    public string Placeholder { get; }

    /// <summary>The path actually in use (which may come from the environment or a default rather than the override).</summary>
    public string Status => _status();

    /// <summary>The override as typed; setting it validates, and persists it when valid.</summary>
    public string? Value
    {
        get => _value;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            var isValid = normalized is null || Directory.Exists(normalized);

            SetProperty(ref _value, isValid ? normalized : value);
            Error = isValid ? null : "That folder doesn't exist.";
            if (isValid)
            {
                _persist(normalized);
                OnPropertyChanged(nameof(Status));
            }
        }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Remote.Adb.Core.Settings;

/// <inheritdoc />
internal sealed class SettingsStore : ISettingsStore
{
    /// <summary>
    /// The environment variable that, when set, points the app at a different settings file — e.g. a scratch file for
    /// a test run, so it can't touch (or act on) the real settings.
    /// </summary>
    public const string FilePathVariable = "REMOTE_ADB_SETTINGS";

    // Enums are written as names (e.g. "Dark") so the file is human-readable and stable against enum
    // reordering. Reflection-based serialization keeps "add a setting" to a single edit on SettingsModel.
    // NOTE: if Core is ever trim/AOT-published, swap to a source-generated JsonSerializerContext.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly ILogger<SettingsStore> _logger;

    public SettingsStore(ILogger<SettingsStore> logger, string? filePath = null)
    {
        _logger = logger;
        _filePath = filePath ?? DefaultFilePath();
        _logger.LogInformation("Using settings file {Path}.", _filePath);
    }

    /// <summary>Where an unreadable (corrupt) settings file is moved before defaults replace it: <c>settings.json.bak</c>.</summary>
    internal string BackupFilePath => _filePath + ".bak";

    /// <summary>The default settings path: <c>{ApplicationData}/Remote.Adb/settings.json</c>.</summary>
    public static string DefaultFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Remote.Adb", "settings.json");
    }

    /// <inheritdoc />
    public SettingsModel Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogDebug("No settings file at {Path}; using defaults.", _filePath);
                return new SettingsModel();
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<SettingsModel>(json, JsonOptions) ?? new SettingsModel();
        }
        catch (JsonException exception)
        {
            // The next save would overwrite the file with defaults, losing whatever the user had — keep it aside.
            _logger.LogWarning(exception, "Settings file {Path} is not valid JSON; using defaults. The unreadable file is kept as {Backup}.", _filePath, BackupFilePath);
            TryBackUpCorruptFile();
            return new SettingsModel();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Could not read settings from {Path}; using defaults.", _filePath);
            return new SettingsModel();
        }
    }

    /// <summary>
    /// The settings file to use: the <see cref="FilePathVariable"/> override (made absolute, so it doesn't depend on
    /// the working directory the app was launched from) when set, otherwise <see cref="DefaultFilePath"/>.
    /// </summary>
    internal static string ResolveFilePath(Func<string, string?> getVariable)
    {
        var overridePath = getVariable(FilePathVariable);
        return string.IsNullOrWhiteSpace(overridePath) ? DefaultFilePath() : Path.GetFullPath(overridePath.Trim());
    }

    /// <inheritdoc />
    public void Save(SettingsModel settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);

            // Write to a sibling temp file then move it over the target, so a crash mid-write can never leave
            // a half-written settings.json (the move is atomic on a single volume).
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Could not write settings to {Path}; the change won't survive a restart.", _filePath);
        }
    }

    // Moves an unparseable settings file to BackupFilePath (replacing an older backup) so it can be recovered by hand.
    private void TryBackUpCorruptFile()
    {
        try
        {
            File.Move(_filePath, BackupFilePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Could not back up the unreadable settings file {Path}.", _filePath);
        }
    }
}

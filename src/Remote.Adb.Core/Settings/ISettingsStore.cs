namespace Remote.Adb.Core.Settings;

/// <summary>
/// Reads and writes the persisted <see cref="SettingsModel"/> on disk. The read path is tolerant (a missing
/// or corrupt file yields defaults, never throws — a corrupt one is first moved aside to <c>settings.json.bak</c>);
/// the write path is atomic.
/// </summary>
internal interface ISettingsStore
{
    /// <summary>
    /// Loads persisted settings; never throws — a missing or unreadable file yields defaults, and a corrupt one is
    /// moved to a <c>.bak</c> sibling first so the next save doesn't destroy it.
    /// </summary>
    SettingsModel Load();

    /// <summary>Persists settings atomically (temp file + move). Failures are logged, not thrown.</summary>
    void Save(SettingsModel settings);
}

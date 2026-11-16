namespace Remote.Adb.Core.Settings;

/// <summary>
/// <see cref="ISettingsService"/> backed by an <see cref="ISettingsStore"/>: the persisted model is loaded
/// once on construction and re-saved whenever a setting changes. Every change (compare, assign, save) runs under
/// one lock, so concurrent writers neither lose updates nor race each other's save.
/// </summary>
internal sealed class SettingsService : ISettingsService
{
    private readonly Lock _gate = new();
    private readonly SettingsModel _model;
    private readonly ISettingsStore _store;

    public SettingsService(ISettingsStore store)
    {
        _store = store;
        _model = _store.Load();
    }

    /// <inheritdoc />
    public string? AvdHome
    {
        get => _model.AvdHome;
        set => SetOverride(() => _model.AvdHome, value, normalized => _model.AvdHome = normalized);
    }

    /// <inheritdoc />
    public AppDensity Density
    {
        get => _model.Density;
        set => Set(() => _model.Density, value, density => _model.Density = density);
    }

    /// <inheritdoc />
    public string? JavaHome
    {
        get => _model.JavaHome;
        set => SetOverride(() => _model.JavaHome, value, normalized => _model.JavaHome = normalized);
    }

    /// <inheritdoc />
    public double LogPanelHeight
    {
        get => _model.LogPanelHeight;
        set => Set(() => _model.LogPanelHeight, value, height => _model.LogPanelHeight = height);
    }

    /// <inheritdoc />
    public bool LogPanelOpen
    {
        get => _model.LogPanelOpen;
        set => Set(() => _model.LogPanelOpen, value, open => _model.LogPanelOpen = open);
    }

    /// <inheritdoc />
    public string? SdkRoot
    {
        get => _model.SdkRoot;
        set => SetOverride(() => _model.SdkRoot, value, normalized => _model.SdkRoot = normalized);
    }

    /// <inheritdoc />
    public AppTheme Theme
    {
        get => _model.Theme;
        set => Set(() => _model.Theme, value, theme => _model.Theme = theme);
    }

    /// <inheritdoc />
    public bool TunnelAutoConnect
    {
        get => _model.TunnelAutoConnect;
        set => Set(() => _model.TunnelAutoConnect, value, autoConnect => _model.TunnelAutoConnect = autoConnect);
    }

    /// <inheritdoc />
    public string? TunnelHost
    {
        get => _model.TunnelHost;
        set => SetOverride(() => _model.TunnelHost, value, normalized => _model.TunnelHost = normalized);
    }

    /// <inheritdoc />
    public int TunnelLocalPort
    {
        get => _model.TunnelLocalPort;
        set => Set(() => _model.TunnelLocalPort, value, port => _model.TunnelLocalPort = port);
    }

    /// <inheritdoc />
    public int TunnelRemotePort
    {
        get => _model.TunnelRemotePort;
        set => Set(() => _model.TunnelRemotePort, value, port => _model.TunnelRemotePort = port);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> WirelessAddresses
    {
        // The stored list is replaced, never mutated, so handing it out is safe.
        get => _model.WirelessAddresses;
        set => UpdateWirelessAddresses(_ => value);
    }

    // A blank or whitespace path means "no override" — store it as null so it round-trips as absent.
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // The one compare / assign / save path every setter shares: saves only when the value actually changes.
    private void Set<T>(Func<T> current, T value, Action<T> assign)
    {
        lock (_gate)
        {
            if (EqualityComparer<T>.Default.Equals(current(), value))
            {
                return;
            }

            assign(value);
            _store.Save(_model);
        }
    }

    private void SetOverride(Func<string?> current, string? value, Action<string?> assign) =>
        Set(current, Normalize(value), assign);

    /// <inheritdoc />
    public void UpdateWirelessAddresses(Func<IReadOnlyList<string>, IReadOnlyList<string>> update)
    {
        lock (_gate)
        {
            var updated = update(_model.WirelessAddresses);
            if (_model.WirelessAddresses.SequenceEqual(updated))
            {
                return;
            }

            _model.WirelessAddresses = [.. updated];
            _store.Save(_model);
        }
    }
}

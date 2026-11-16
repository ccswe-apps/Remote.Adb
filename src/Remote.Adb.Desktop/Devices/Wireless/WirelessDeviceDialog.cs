using Remote.Adb.Desktop.Common;

namespace Remote.Adb.Desktop.Devices.Wireless;

/// <inheritdoc />
public sealed class WirelessDeviceDialog : IWirelessDeviceDialog
{
    private readonly Func<AddWirelessDeviceViewModel> _viewModelFactory;

    public WirelessDeviceDialog(Func<AddWirelessDeviceViewModel> viewModelFactory)
    {
        _viewModelFactory = viewModelFactory;
    }

    /// <inheritdoc />
    public Task<bool> ShowAsync() => DialogHost.ShowAsync<AddWirelessDeviceWindow>(_viewModelFactory());
}

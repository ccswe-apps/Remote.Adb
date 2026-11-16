namespace Remote.Adb.Desktop.Devices.Wireless;

/// <summary>
/// Shows the modal "Add wireless device" dialog, keeping <see cref="DevicesViewModel"/> free of any window handling.
/// </summary>
public interface IWirelessDeviceDialog
{
    /// <summary>Opens the dialog and returns <see langword="true"/> if a device was connected.</summary>
    Task<bool> ShowAsync();
}

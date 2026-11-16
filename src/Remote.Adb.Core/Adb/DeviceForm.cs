using JetBrains.Annotations;

namespace Remote.Adb.Core.Adb;

/// <summary>The device's form factor, derived from <c>ro.build.characteristics</c> — used to pick its icon.</summary>
[PublicAPI]
public enum DeviceForm
{
    /// <summary>A phone (the default when nothing more specific is reported).</summary>
    Phone,

    /// <summary>A tablet.</summary>
    Tablet,

    /// <summary>A Wear OS watch.</summary>
    Watch,

    /// <summary>An Android TV / Google TV device.</summary>
    Television,

    /// <summary>An Android Automotive head unit.</summary>
    Automotive,
}

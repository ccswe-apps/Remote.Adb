namespace Remote.Adb.Console;

/// <summary>The parsed arguments of <c>emulator create</c>.</summary>
/// <param name="Name">The new AVD's name (<c>-n</c>).</param>
/// <param name="Package">The system-image package to build it from (<c>-k</c>).</param>
/// <param name="Device">The device profile id (<c>-d</c>).</param>
/// <param name="Overrides">Config values to write once it's created (<c>--key value</c>).</param>
internal sealed record AvdCreateArguments(string Name, string Package, string Device, IReadOnlyDictionary<string, string> Overrides);

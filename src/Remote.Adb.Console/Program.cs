using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Remote.Adb.Console;
using Remote.Adb.Core;
using Remote.Adb.Core.Adb.Wireless;
using Remote.Adb.Core.Common;
using Remote.Adb.Core.Emulators;
using Remote.Adb.Core.Settings;
using Remote.Adb.Core.Tunnel;

// --verbose (anywhere on the command line) echoes every tool run and its output; strip it before dispatch.
args = ConsoleArguments.StripVerbose(args, out var verbose);

var services = new ServiceCollection();
services.AddLogging(builder => builder
    .AddSimpleConsole(options => options.SingleLine = true)
    .SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Warning)
    // The tunnel command prints every status change itself; its Faulted warning would print the same thing twice.
    .AddFilter(typeof(TunnelService).FullName, verbose ? LogLevel.Debug : LogLevel.Error));
services.AddRemoteAdbCore();

// Async disposal: the tunnel service is IAsyncDisposable only, which a synchronous Dispose rejects.
await using var provider = services.BuildServiceProvider();

if (args.Length == 0)
{
    PrintUsage(Console.Error);
    return 1;
}

try
{
    return args[0] switch
    {
        "emulator" => await HandleEmulatorAsync(
            args[1..],
            provider.GetRequiredService<IEmulatorService>(),
            provider.GetRequiredService<IAvdConfigStore>(),
            provider.GetRequiredService<IAvdProvisioningService>()),
        "help" or "--help" or "-h" => Help(),
        "tunnel" => await HandleTunnelAsync(
            args[1..],
            provider.GetRequiredService<ITunnelService>(),
            provider.GetRequiredService<ISettingsService>()),
        "wireless" => await HandleWirelessAsync(
            args[1..],
            provider.GetRequiredService<IWirelessAdbService>(),
            provider.GetRequiredService<ISettingsService>()),
        _ => Unknown(args[0]),
    };
}
catch (ProcessLaunchException exception)
{
    return Fail(exception.Message);
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static async Task<int> HandleEmulatorAsync(string[] args, IEmulatorService emulators, IAvdConfigStore configStore, IAvdProvisioningService provisioning)
{
    var command = args.Length > 0 ? args[0] : string.Empty;
    var operand = ConsoleArguments.Operand(args);

    switch (command)
    {
        case "list":
        {
            var devices = await emulators.ListAsync();
            if (devices.Count == 0)
            {
                Console.WriteLine("No AVDs found.");
                return 0;
            }

            foreach (var device in devices)
            {
                var status = device.IsRunning ? $"running ({device.Serial})" : "stopped";
                var tag = device.Tag is null ? string.Empty : $" — {device.Tag}";
                Console.WriteLine($"{device.DisplayName}  ({device.Name}){tag}  [{status}]");
            }

            return 0;
        }
        case "start":
        {
            if (operand is null)
            {
                return Fail("Usage: emulator start <avd-name>");
            }

            await emulators.StartAsync(operand);
            Console.WriteLine($"Starting {operand}...");
            return 0;
        }
        case "stop":
        {
            if (operand is null)
            {
                return Fail("Usage: emulator stop <serial>");
            }

            var result = await emulators.StopAsync(operand);
            return Report(result.Success, result.Success ? $"Stopped {operand}." : $"Could not stop {operand}: {result.Error}");
        }
        case "info":
        {
            if (operand is null)
            {
                return Fail("Usage: emulator info <avd-name>");
            }

            var configuration = configStore.Read(operand);
            if (configuration is null)
            {
                return Fail($"No AVD found with id '{operand}'.");
            }

            PrintConfiguration(configuration);
            return 0;
        }
        case "edit":
        {
            if (operand is null)
            {
                return Fail("Usage: emulator edit <avd-name> --<key> <value> [--<key> <value>…]  (a bare --<key> clears it; use --<key>=<value> for a value starting with --)");
            }

            if (!ConsoleArguments.TryParseEdits(args[2..], out var edits, out var parseError))
            {
                return Fail(parseError!);
            }

            if (edits!.IsEmpty)
            {
                return Fail("Nothing to change. Pass --<key> <value> to set, or a bare --<key> to clear.");
            }

            var edited = configStore.Write(operand, edits.Changes, edits.Removals);
            if (edited is null)
            {
                return Fail($"No AVD found with id '{operand}'.");
            }

            PrintConfiguration(edited);
            return 0;
        }
        case "images":
        {
            var images = await provisioning.ListInstalledImagesAsync();
            if (images.Count == 0)
            {
                Console.WriteLine("No installed system images. Install one with the SDK manager.");
                return 0;
            }

            foreach (var image in images)
            {
                Console.WriteLine($"{image.Package}  (API {image.ApiLevel}, {image.Tag}, {image.Abi})");
            }

            return 0;
        }
        case "devices":
        {
            foreach (var profile in await provisioning.ListDevicesAsync())
            {
                var specs = profile.ScreenWidth is { } width && profile.ScreenHeight is { } height
                    ? $"  {width}x{height}" + (profile.Density is { } density ? $" {density}dpi" : string.Empty)
                    : string.Empty;
                var api = string.IsNullOrEmpty(profile.SupportedApi) ? string.Empty : $" API {profile.SupportedApi}";
                var oem = profile.Oem is null ? string.Empty : $"  — {profile.Oem}";
                Console.WriteLine($"{profile.Id,-26}[{profile.FormFactor,-9}] {profile.Name}{api}{specs}{oem}");
            }

            return 0;
        }
        case "create":
        {
            if (!ConsoleArguments.TryParseCreate(args[1..], out var create, out var createError))
            {
                return Fail(createError!);
            }

            var createResult = await provisioning.CreateAsync(create!.Name, create.Package, create.Device);
            if (!createResult.Success)
            {
                return Fail(createResult.Error ?? "avdmanager could not create the AVD. Check the SDK installation.");
            }

            if (create.Overrides.Count > 0)
            {
                configStore.Write(create.Name, create.Overrides);
            }

            Console.WriteLine($"Created {create.Name}.");
            var created = configStore.Read(create.Name);
            if (created is not null)
            {
                PrintConfiguration(created);
            }

            return 0;
        }
        case "delete":
        {
            if (operand is null)
            {
                return Fail("Usage: emulator delete <avd-name>");
            }

            var deleted = await provisioning.DeleteAsync(operand);
            return Report(deleted, deleted ? $"Deleted {operand}." : $"Could not delete '{operand}'.");
        }
        default:
            return Fail("Usage: emulator <list|info|edit|images|devices|create|delete|start|stop> …");
    }
}

// Opens the reverse tunnel and blocks until Ctrl+C (or the tunnel ends) — keep the window open.
// The host falls back to the saved setting when omitted.
static async Task<int> HandleTunnelAsync(string[] args, ITunnelService tunnel, ISettingsService settings)
{
    var host = args.Length > 0 ? args[0] : settings.TunnelHost;
    if (string.IsNullOrWhiteSpace(host))
    {
        return Fail("Usage: tunnel [host]   (no host configured — pass one, or set it in the desktop app)");
    }

    using var cancel = new CancellationTokenSource();

    // The first Ctrl+C closes the tunnel cleanly; a second one (if closing hangs) terminates as usual.
    void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs eventArgs)
    {
        eventArgs.Cancel = !cancel.IsCancellationRequested;
        cancel.Cancel();
    }

    Console.CancelKeyPress += OnCancelKeyPress;
    try
    {
        return await TunnelCommand.RunAsync(tunnel, host, Console.Out, cancel.Token);
    }
    finally
    {
        Console.CancelKeyPress -= OnCancelKeyPress;
    }
}

static async Task<int> HandleWirelessAsync(string[] args, IWirelessAdbService wireless, ISettingsService settings)
{
    var command = args.Length > 0 ? args[0] : string.Empty;

    switch (command)
    {
        case "connect" when args.Length == 2:
        {
            var result = await wireless.ConnectAsync(args[1]);
            return Report(result.Success, result.Message);
        }
        case "disconnect" when args.Length == 2:
        {
            var result = await wireless.DisconnectAsync(args[1]);
            return Report(result.Success, result.Message);
        }
        case "pair" when args.Length is 2 or 3:
        {
            // Prompt for the code rather than requiring it on the command line, where it would land in shell history.
            var code = args.Length == 3 ? args[2] : Prompt("Pairing code: ");
            var result = await wireless.PairAsync(args[1], code);
            return Report(result.Paired, result.Message);
        }
        case "enable" when args.Length == 2:
        {
            var result = await wireless.EnableWirelessAsync(args[1]);
            return Report(result.Success, result.Success ? $"Connected at {result.Address}; the device can be unplugged." : result.Message);
        }
        case "mdns":
        {
            var services = await wireless.DiscoverAsync();
            if (services.Count == 0)
            {
                Console.WriteLine("No wireless debugging services found (is the device on this network with Wireless debugging on?).");
                return 0;
            }

            foreach (var service in services)
            {
                Console.WriteLine($"{service.Type,-8} {service.Address,-22} {service.Instance}");
            }

            return 0;
        }
        case "reconnect":
        {
            var saved = settings.WirelessAddresses.Count;
            var connected = await wireless.ReconnectSavedAsync();

            // Nothing saved is not a failure; saved devices that all failed to come back is.
            return Report(saved == 0 || connected > 0, $"{connected} of {saved} saved wireless device(s) connected.");
        }
        default:
            PrintUsage(Console.Error);
            return 1;
    }
}

static int Help()
{
    PrintUsage(Console.Out);
    return 0;
}

static void PrintConfiguration(AvdConfiguration config)
{
    static void Line(string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Console.WriteLine($"  {label,-16}{value.Trim()}");
        }
    }

    Console.WriteLine($"{config.DisplayName}  ({config.AvdId})");
    Line("API level", config.ApiLevel?.ToString());
    Line("ABI", config.Abi);
    Line("Image", config.SystemImage);
    Line("Tag", config.Tag);
    Line("Device", config.DeviceName);
    Line("Manufacturer", config.Manufacturer);
    Line("Resolution", config.LcdWidth is { Length: > 0 } width && config.LcdHeight is { Length: > 0 } height ? $"{width} x {height}" : null);
    Line("Density", config.LcdDensity);
    Line("RAM", config.RamSize);
    Line("VM heap", config.VmHeapSize);
    Line("Data partition", config.DataPartitionSize);
    Line("SD card", config.SdCardSize ?? config.HasSdCard);
    Line("CPU cores", config.CpuCores);
    Line("GPU", config.GpuMode ?? config.GpuEnabled);
    Line("Path", config.Path);
}

static void PrintUsage(TextWriter writer)
{
    writer.WriteLine("Remote.Adb console");
    writer.WriteLine();
    writer.WriteLine("Usage (add --verbose to any command to log the tools it runs):");
    writer.WriteLine("  emulator list                 List available AVDs and their running state");
    writer.WriteLine("  emulator info <avd-name>      Show the full configuration of the given AVD");
    writer.WriteLine("  emulator edit <avd-name> …    Set --<key> <value> pairs (bare --<key> clears) and persist");
    writer.WriteLine("  emulator images               List installed system-image packages");
    writer.WriteLine("  emulator devices              List device profiles for create");
    writer.WriteLine("  emulator create -n … -k … -d … [--<key> <value>…]   Create an AVD and apply overrides");
    writer.WriteLine("  emulator delete <avd-name>    Delete the given AVD");
    writer.WriteLine("  emulator start <avd-name>     Launch the given AVD");
    writer.WriteLine("  emulator stop <serial>        Stop the running emulator with the given serial");
    writer.WriteLine("  tunnel [host]                 Open the SSH reverse tunnel (host falls back to the saved setting); Ctrl+C closes it");
    writer.WriteLine("  wireless pair <ip:port> [code]   Pair an Android 11+ device (prompts for the code if omitted)");
    writer.WriteLine("  wireless connect <ip[:port]>  Connect a device over the network and remember it");
    writer.WriteLine("  wireless disconnect <serial>  Disconnect a network device and forget it");
    writer.WriteLine("  wireless enable <usb-serial>  Switch a USB device to Wi-Fi debugging and connect to it");
    writer.WriteLine("  wireless reconnect            Reconnect every remembered device");
    writer.WriteLine("  wireless mdns                 List wireless debugging services adb sees on the network");
}

// Reads a line from stdin. The label goes to stderr so stdout stays clean for piping, and is skipped when input is
// redirected (nobody is there to read it).
static string Prompt(string label)
{
    if (!Console.IsInputRedirected)
    {
        Console.Error.Write(label);
    }

    return Console.ReadLine() ?? string.Empty;
}

// Prints a command's outcome — to stdout on success, stderr on failure — and maps it to the exit code.
static int Report(bool success, string message)
{
    (success ? Console.Out : Console.Error).WriteLine(message);
    return success ? 0 : 1;
}

static int Unknown(string command)
{
    Console.Error.WriteLine($"Unknown command '{command}'.");
    PrintUsage(Console.Error);
    return 1;
}

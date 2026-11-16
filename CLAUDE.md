# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# Project

A cross-platform desktop app to manage ADB (Android Debug Bridge) connections to a remote development server. The end goal is to unify, in one GUI, the workflows previously scattered across manual scripts and steps:

1. **SSH port forwarding** — open a reverse tunnel so a Windows-side `adb` server is reachable from the remote dev host (the remote's `adb` talks to `127.0.0.1:5037`, forwarded back to the local server).
2. **Emulator management** — start, manage, and create Android emulators.
3. **Remote device connection** — connect to Android devices over the network (e.g. Wi-Fi).

The app is C#; the target framework is set in `src/Directory.Build.props`. All functionality lives in a shared **`Remote.Adb.Core`** library exposed through two front-ends: a desktop GUI (**`Remote.Adb.Desktop`**, **Avalonia** + **MVVM** via CommunityToolkit.Mvvm) and a CLI (**`Remote.Adb.Console`**). All three pillars (tunnel, emulators, network devices) have landed; see `docs/ROADMAP.md` for what's next.

## Roadmap & tasks

Planning lives in `docs/`, not here — consult it before starting work, and keep it current as milestones land. **`docs/` tracks future work only**: completed work is evidenced in git history and the code itself, so it is *not* archived here.

- `docs/ROADMAP.md` — vision, the three pillars, and a milestone table of **planned** work in priority order (the source of truth for what's next).
- `docs/tasks/<name>.md` — per-milestone breakdowns with checklists and verification notes. Named by topic, **not numbered** (priority/order lives in the ROADMAP, which links to them).

When a milestone lands, **delete its task doc and remove its ROADMAP row** — don't leave a ✅. Where a task doc and the code disagree, the code wins and the doc needs reconciling.

## Reference: SSH/adb constraints

Hard-won knowledge the tunnel and adb code preserve.

**The tunnel (`TunnelService`):**

- The reverse tunnel is `ssh -o ExitOnForwardFailure=yes -N -R <remote>:127.0.0.1:<local> <host>` (ports from `TunnelRemotePort`/`TunnelLocalPort` in `SettingsModel`). `ExitOnForwardFailure` turns a silent bind failure into a visible non-zero exit.
- Kill the remote `adb` with `pkill -x adb`, **not** `adb kill-server` — `kill-server` does a localhost network round-trip that can hang on a stale/forwarded `127.0.0.1:5037`.
- Use the literal `127.0.0.1` (not `localhost`) for the forward target — Windows OpenSSH may resolve `localhost` to IPv6 `::1`, but the Windows `adb` server binds only IPv4, causing refused connections.
- A remote IntelliJ Android plugin respawns `adb` on Gradle sync and races the bind — hence the kill-then-bind-then-retry loop.

**The local adb server (`Adb/`):**

- **Every adb invocation goes through `IAdbRunner`** — never `IProcessRunner` with `IAndroidSdk.AdbPath` (`AdbInvocationGuardTests` enforces it). Any adb command can start the server, so the runner gives every one the same environment: `ADB_REJECT_KILL_SERVER=1`, which makes a server the app starts refuse a `kill-server` — including one a version-mismatched remote adb sends through the tunnel.
- Because the app's servers reject kills, the app stops the server by **killing its process** (`IAdbServerProcesses`, matched by the configured adb's full path), never `adb kill-server`.
- A newer local adb (SDK upgrade, a different adb on PATH) can't replace a stale server that rejects its kill; the runner detects that (`doesn't match this client` / `rejected by remote server`), kills the server process, and retries once.
- A server started outside the app (Android Studio, a terminal) lacks the variable, so connecting the tunnel replaces it (`IAdbServerService.EnsureOwnServerAsync`). The runner knows the app started the server because adb reports `daemon started successfully`.
- The adb server's port is `ANDROID_ADB_SERVER_PORT` or 5037 (`IAdbRunner.ServerPort`) — not the tunnel's configurable local port.

**Wireless devices (`Adb/Wireless/`):**

- Everything goes through the **local** adb server — the one the tunnel exposes — so a device connected here is also visible to the remote host's adb. mDNS discovery and QR pairing need this machine on the phone's LAN with UDP 5353 allowed for `adb` through the firewall; adb's discovery is IPv4-only.
- `adb connect` **exits 0 even when it fails** — classify by the message (test `already connected to` before `connected to`). `adb mdns check` also exits 0 when discovery is off (available only if the output starts with `mdns daemon version`). `adb pair` failures mostly surface as a generic `error: protocol fault …`, and it blocks with no timeout of its own.
- Pass the pairing code on **stdin** (`adb pair <addr>` with no code prompts for it), never as an argument, so it stays out of logs and the process list. A QR code's password is just a pairing code; its service name and password use `[A-Za-z0-9]` only.
- After pairing, adb auto-connects by the device's guid (= its `_adb-tls-connect` mDNS instance name) only if it has already seen that announcement — `PairAsync` connects explicitly when it hasn't.
- An Android 11+ device's connect port changes every time Wireless debugging is toggled, so TLS connect addresses aren't remembered for reconnects — only addresses typed by hand and legacy `adb tcpip` devices (port 5555) are.
- Switching a USB device to Wi-Fi: read its address **before** `adb tcpip` (adbd restarting drops the USB link) and only from a `wlan*` interface (never mobile data or a VPN), then retry the connect briefly.
- Disconnecting an mDNS-connected device may not stick: adb's mDNS auto-connect can reconnect it.

## Git Commits

Do not include co-author trailers or any mention of Claude in commit messages.

## Build & Run Commands

All projects live under `src/`; the solution is `src/Remote.Adb.slnx`.

```bash
# Build
dotnet build src/Remote.Adb.slnx --configuration Release

# Run the desktop app
dotnet run --project src/Remote.Adb.Desktop

# Run the console app
dotnet run --project src/Remote.Adb.Console -- <args>

# Run all tests
dotnet test src/Remote.Adb.slnx

# Run a specific test
dotnet test src/Remote.Adb.slnx --filter "FullyQualifiedName~ClassName"
```

The SDK is pinned via the root `global.json`. `src/Directory.Build.props` applies `LangVersion=default`, `ImplicitUsings=enable`, and `Nullable=enable` solution-wide, and references JetBrains.Annotations and Nerdbank.GitVersioning (version derived from git history).

## Package management

Package versions are centrally managed via **Central Package Management** (`src/Directory.Packages.props`, `ManagePackageVersionsCentrally=true`). To add or update a dependency, add a `<PackageVersion Include="X" Version="N" />` in `Directory.Packages.props` and a version-less `<PackageReference Include="X" />` in the project (or `Directory.Build.props` for solution-wide refs). Never put `Version=` on a `<PackageReference>` — it errors with NU1008.

When a CCSWE.Avalonia.* library publishes, `.github/workflows/update-dependencies.yml` (ccswe-apps/actions `dotnet-update-dependencies.yml`) opens an andy PR bumping its `<PackageVersion>`; non-major bumps auto-merge. Dependabot excludes `CCSWE.*` from its cooldown and remains the fallback.

## Architecture

Projects in `src/Remote.Adb.slnx`:

- **`Remote.Adb.Core`** — class library holding all domain logic: models, services (process execution, Android SDK location, emulator management, adb devices, wireless connect/pair (`Adb/Wireless/`), SSH tunnel), and the `AddRemoteAdbCore()` DI registration extension. Both front-ends depend on it; it has no UI dependency.
- **`Remote.Adb.Desktop`** — the Avalonia desktop GUI (`WinExe`), MVVM.
- **`Remote.Adb.Console`** — the CLI front-end exposing the same Core functionality.
- **`Remote.Adb.Core.UnitTests`** / **`Remote.Adb.Desktop.UnitTests`** — NUnit tests for Core / Desktop.

Both front-ends compose a `Microsoft.Extensions.DependencyInjection` service provider and call `AddRemoteAdbCore()` to register the shared services. Keep logic in Core; the GUI and CLI are thin shells over it.

**Logging.** Code logs through `ILogger<T>` — there is no separate logging API. The desktop host adds `builder.Logging.AddRemoteAdbLogStore()`, which captures `Remote.Adb` logging (Information and up) into the in-memory `ILogStore` (Core `Logging/`) that the docked log panel (Desktop `Logging/`) and the shell's status bar (`Shell/StatusBarView`, which toggles the panel) display; the console logs to stdout (`--verbose` for Debug). `ProcessRunner` logs every external command it runs — completion with exit code and duration, plus its output — so callers don't need to log a tool's failure themselves. Wrap routine, automatic work (list polling, launch waits) in `CommandLogScope.Background()` so its commands log at Debug and stay out of the panel.

The desktop app follows Avalonia's **MVVM** conventions:

- `Program.Main` builds a **.NET Generic Host** via `DesktopApplication.CreateBuilder<App>` (from the **CCSWE.Avalonia.Hosting** package), registers services, and runs it — the host owns DI and the classic desktop lifetime. `BuildAvaloniaApp()` mirrors the same Avalonia configuration (minus the host) for the XAML previewer; `WithDeveloperTools()` is added only in `DEBUG`. Fonts/type scale come from the CCSWE.Avalonia.Material theme, not a base theme.
- `App.OnFrameworkInitializationCompleted` is the composition root — the host injects the service provider (`IServiceProviderAccessor`), then the app applies the persisted theme/density, registers the DI-backed `ViewLocator`, and resolves `MainWindow` from DI.
- **View resolution is source-generated** via the **CCSWE.Avalonia.ViewLocator** package: `ViewLocator` is an empty `[GenerateViewLocator(typeof(ViewModelBase))]` partial that the generator fills in at compile time, mapping each `FooViewModel` to the `FooView` in the **same namespace** (e.g. `DevicesViewModel` → `DevicesView` — so a VM and its view live together in one feature folder) and resolving the view from DI. View models must derive from `ViewModelBase` for the locator to match.
- `ViewModelBase` derives from CommunityToolkit.Mvvm's `ObservableObject`. Use the toolkit's source generators (`[ObservableProperty]`, `[RelayCommand]`) for bindable state and commands.
- **Compiled bindings are on by default** (`AvaloniaUseCompiledBindingsByDefault=true`) — XAML bindings need a declared `x:DataType`, and binding errors surface at compile time.

### View models and services stay UI-free

A view model or service must **not** depend directly on an Avalonia UI control or threading primitive (`WindowNotificationManager`, `DispatcherTimer`, `TopLevel`, `Dispatcher`, …). Introduce a thin abstraction in the relevant `Common/` folder and a UI-side **adapter** that implements it; the adapter — not the consumer — owns the Avalonia type and any UI-thread marshaling. This keeps view models and services unit-testable with plain NUnit + Moq (no Avalonia.Headless harness). Examples: `ITimerFactory`/`IDispatcherTimer` wrap `DispatcherTimer` (`Common/Threading/`), and `INotificationSink` wraps `WindowNotificationManager` (`Common/Notifications/`).

### Project organization — feature-first vertical slices

Both `Remote.Adb.Desktop` and `Remote.Adb.Core` are organized **feature-first**: a feature's views, view models, and services live together in a feature folder (e.g. `Adb/`, `Emulators/`, `Devices/`, `Tunnel/`, `Settings/`), with `Common/` for genuinely cross-feature pieces (base classes, process/SDK plumbing, reusable controls, converters) and Desktop's `Shell/` for the app frame + navigation. Namespaces follow folders; a piece used by exactly one feature lives *in* that feature, not in `Common/`.

**Decompose views into small, single-purpose `UserControl`s.** A page is a thin shell that composes smaller pieces (a list view, a row card, a reusable overlay, a detail pane) — never one giant XAML file. Extract a piece even if it isn't reused yet.

When adding a screen: create `FooView.axaml` (+ `.axaml.cs`) and `FooViewModel.cs` (deriving from `ViewModelBase`) in the relevant feature folder of `Remote.Adb.Desktop`, and register the view in DI; the source-generated `ViewLocator` wires them by name. Domain models and services belong in the matching feature folder of `Remote.Adb.Core`.

### Scrollable views — inset the content, not the scroll container

The page inset for a scrollable view goes on the **scrolled content's `Margin`** (the inner `ItemsControl`/`StackPanel`), never on the `ScrollViewer` or an outer container that wraps it:

- **Padding on the `ScrollViewer`** leaves the bottom padding *outside* the scrollable extent (Avalonia's `ScrollContentPresenter`), so the last items are unreachable.
- **Margin on an outer container** (or on the `ScrollViewer`/`TabControl` itself) insets the scrollbar too, so it floats off the panel edge instead of sitting against it.

Pattern: a full-bleed scroll container (no margin/padding) → scrolled content carries the inset as its `Margin`. The scrollbar reaches the edge and every item is reachable. Also, a scrollable screen hosted in the CCSWE `DrawerPage` must bound its `ScrollViewer` via a `Grid` `*` row (or a `Panel`), not a `DockPanel`, or it won't get a bounded viewport height.

# Testing

Tests use **NUnit**.

## Class organization

- Outer class name: `<ClassUnderTest>Tests`, decorated with `[SuppressMessage("ReSharper", "InconsistentNaming")]`. The outer class is NOT `sealed` — nested classes inherit from it.
- Nested classes group tests by method or scenario: `When_<MethodName>_Is_Called`, inheriting the outer class.
- Test methods describe expected behavior: `It_<expected_behavior>` (e.g., `It_Adds_UserAgent_Header`)

```csharp
public class SomeServiceTests
{
    public class When_GetAsync_Is_Called : SomeServiceTests
    {
        [Test]
        public async Task It_returns_expected_result() { ... }
    }
}
```

## Arrange-Act-Assert

Follow the AAA pattern. Use blank lines to separate sections — do **not** use `// Arrange`, `// Act`, `// Assert` comments.

## Mocking

- Use **Moq** for mocking
- `ILogger` should be mocked using the `LoggerFake` class, not `new Mock<ILogger>()`
- Prefer `ReturnsAsync(...)` and `ThrowsAsync(...)` over manually setting up async mock methods

## Running the app for manual testing

Always launch it with `REMOTE_ADB_SETTINGS` pointing at a scratch file. Otherwise it uses the real settings file — and if tunnel auto-connect is on, every launch opens the user's real SSH tunnel (killing the remote `adb`). See `docs/wslg-gui-debugging.md` §10 for driving the window on Windows.

# Coding Standards

Follow standard C# conventions ([source](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)).

## Formatting

- 4-space indentation (no tabs)
- Allman brace style — opening and closing braces on their own lines
- Always use curly braces for control flow (`if`, `for`, `foreach`, `while`, etc.) — never omit them for single-line bodies
- One statement per line; one declaration per line
- One blank line between members; no consecutive blank lines
- Space after flow-control keywords (`if (`, `for (`); no space after method names (`Method(`)

## Naming

- PascalCase: classes, methods, properties, constants, namespaces, public fields, record primary constructor parameters
- camelCase: local variables, parameters
- `_camelCase`: private fields (underscore prefix)
- `I` prefix for interfaces (e.g. `IEmulatorService`)
- Two-character acronyms are uppercase (`IO`, `UI`); longer acronyms use PascalCase (`Http`, `Json`)
- Use `nameof()` instead of string literals for member/property names

## File organization

- One type per file; file named `{TypeName}.cs`
- Partial classes use `{ClassName}.{Part}.cs`
- File-scoped namespaces (`namespace Foo;`), aligned with folder structure
- `using` directives outside namespace declarations, ordered `System` first, then third-party, then project namespaces
- A per-project `Usings.cs` holds global usings (e.g. a test project's global `using NUnit.Framework;`)

## Access modifiers

- Always explicit (no implicit `private` or `internal`)
- `internal` for implementation details
- `[PublicAPI]` (JetBrains.Annotations, referenced solution-wide) on intentionally public API surface
- `[ExcludeFromCodeCoverage]` on composition-only types (the `AddRemoteAdbCore()` registration, the Avalonia `App` composition root, and similar)

## Language style

- Use `var` for all local variables where the type can be inferred
- Use language keywords for built-in types (`string`, `int`, not `String`, `Int32`)
- Prefer string interpolation (`$"..."`) over concatenation
- Use `&&`/`||`, not `&`/`|`, for logical comparisons
- Use `async`/`await` for async code; avoid `.Result` or `.Wait()`
- Prefer expression-bodied members for single-line getters/methods
- Nullable reference types are enabled solution-wide (`Nullable=enable` in `Directory.Build.props`); respect nullability annotations

## Class member order

Within a class, group members by kind in this order, and alphabetize strictly by name within each group regardless of access modifier:

1. Constants / `static readonly` fields
2. Instance fields
3. Constructors
4. Properties
5. Methods

Access modifiers do not affect ordering — when you go looking for `GetFoo()` you don't care whether it's public, internal, or private, so `CreateFoo()` comes before `GetFoo()` regardless. Nested types go at the bottom of the file, after all members of the outer type.

## Shared code (DRY)

Write a piece of logic once and share it; don't copy it into a second place.

- **One runner per external tool.** Every invocation of a given tool goes through its single shared runner — `IAdbRunner` for adb — so cross-cutting behavior (environment, logging, recovery) lives in one place. Don't call `IProcessRunner` with a tool path a runner already owns.
- **Extract before you copy.** When you're about to write something that already exists — a poll loop, a parse step, a view-model command pattern, a dialog plumbing step — extract a shared helper (or reuse the one that's there) instead. Reviews treat duplication as a defect.
- **Place it by reach.** A helper used by one feature lives in that feature's folder; one used across features goes in `Common/` (see feature-first organization above).

## Frozen collections for static lookups

Any `static readonly` `HashSet<T>` or `Dictionary<TKey, TValue>` that is never mutated after construction should be a `FrozenSet<T>` / `FrozenDictionary<TKey, TValue>` (`System.Collections.Frozen`), built via `.ToFrozenSet(comparer)` / `.ToFrozenDictionary()`. Lookups are faster and the frozen type signals "immutable lookup" at the type level.

## Prefer spans for multi-step string manipulation

When a method performs **more than one** manipulation over the same string source — splitting, trimming, slicing, scanning, `StartsWith`/`IndexOf`/comparisons — operate over `ReadOnlySpan<char>` instead of allocating intermediate strings. Take `source.AsSpan()` once, iterate lines with `EnumerateLines()` (no `string[]`, and it handles `\r\n`), tokenize/slice with span operations (`Trim`, `IndexOf`, `SequenceEqual`, range slicing), and call `.ToString()` **only** on the values you actually keep. The `*OutputParser` types in `Remote.Adb.Core` are the canonical example. A single trivial operation on a string doesn't need this; the rule is about chains of manipulation on one source.

This is the parse side only. The byte→string boundary itself (`StreamReader.ReadToEndAsync`) is a separate, larger optimization (read `BaseStream` into pooled buffers, decode to a `char[]`, parse the span) — don't undertake it ad hoc; it changes process-IO plumbing and the `string`-typed error/diagnostic surfaces (`ProcessResult.StandardError`, `IProcessSession.StandardError`).

## XML documentation

In library projects that enable `GenerateDocumentationFile`, document public/internal types and members; use `<inheritdoc />` for interface implementations where the interface doc suffices. Elsewhere, add docs only where they clarify intent.
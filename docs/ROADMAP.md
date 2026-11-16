# Remote.Adb — Roadmap

Future work only. Completed work is evidenced in git history and the code itself, so it isn't tracked here — and
a milestone's task doc is deleted when the milestone lands. Order in the milestone table is the priority.

## Vision

A single tool — desktop GUI and console, both over a shared `Remote.Adb.Core` library — to manage
ADB connections to a remote development server. It replaces the workflows previously encoded in
manual scripts and steps.

## Pillars

1. **SSH port forwarding** — open a reverse tunnel so a local `adb` server is reachable from the
   remote dev host (`ssh -o ExitOnForwardFailure=yes -N -R <remote>:127.0.0.1:<local> <host>`), with
   kill-then-bind-then-retry handling for the IntelliJ adb-respawn race.
2. **Emulator management** — list, start, stop, create, view/edit, and delete Android emulators.
3. **Remote device connection** — connect to Android devices over the network (e.g. Wi-Fi).

## Milestones

| Milestone | Notes |
|-----------|-------|
| **SDK Manager UI** (list / download / remove system images & packages via `sdkmanager`) | [tasks/sdk-manager.md](tasks/sdk-manager.md); unblocks creating from a not-yet-installed image. |
| **DI lifetimes / stop manual construction** | [tasks/di-lifetimes.md](tasks/di-lifetimes.md); deferred until a concrete driver — scope view models to their owning window/view and resolve currently-`new`'d dialog windows/VMs from DI. |

## Working notes

- All domain logic lives in `Remote.Adb.Core`; the GUI and CLI are thin shells that resolve services
  from DI (`AddRemoteAdbCore()`). New capability lands in Core first, then is surfaced in both heads.
- All three pillars have landed: the SSH reverse tunnel, emulator management, and network device connection
  (connect, pair by code or QR, discovery, USB-to-Wi-Fi). See the "SSH/adb constraints" section of `CLAUDE.md`
  for the hard-won SSH/adb details the implementation preserves.
- To debug the desktop UI (catch runtime layout/XAML bugs a clean build misses), drive it under WSLg
  with screenshots — see [wslg-gui-debugging.md](wslg-gui-debugging.md) and the reproducible data
  harness at [tools/setup-fake-avd-harness.sh](tools/setup-fake-avd-harness.sh).
- Possible enhancement: **one device, several rows.** A phone can show up as its USB serial, as `ip:port`, and as
  `adb-<guid>._adb-tls-connect._tcp` at once; grouping rows by `ro.serialno` would collapse them.
- Possible enhancement: expose the list auto-refresh interval (currently the `DevicesViewModel.RefreshInterval`
  constant) as a persisted setting.

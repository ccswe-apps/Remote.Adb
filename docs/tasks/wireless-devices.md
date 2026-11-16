# Wireless devices: discovery, QR pairing, Wi-Fi debugging from USB

Pillar 3's remaining work. Connecting by address, pairing with a code, disconnecting, and reconnecting remembered
addresses after an adb restart are in (`Remote.Adb.Core/Adb/Wireless/`, the Devices page's **Add Wireless Device**
dialog, the card's **Disconnect**, and `wireless …` in the console). What's left builds on that.

## Remaining work

- [ ] **mDNS discovery (B3).** List the wireless-debugging services the local adb server sees
  (`IWirelessAdbService.DiscoverAsync` / `WirelessOutputParser.ParseMdnsServices` already exist) on the dialog's
  Connect tab, refreshed every ~2 s via `ITimerFactory` while the tab is visible. A `_adb-tls-pairing._tcp` row
  pre-fills the Pair tab's address; a `_adb-tls-connect._tcp` or `_adb._tcp` row connects. Check `adb mdns check`
  first (it exits 0 even when discovery is off — OK only if the output starts with `mdns daemon version`) and show
  a hint instead of an empty list when it's unavailable.
  - Polling outlives a closed dialog today: add `IDialogViewModel.OnClosed()` (default no-op), call it from
    `DialogHost.ShowAsync` after `ShowDialog` returns, and stop the timer there. **Don't** make the dialog VM
    `IDisposable` — it's a transient resolved from the root container, which would keep every instance alive.
- [ ] **QR pairing (B4).** Generate `WIFI:T:ADB;S:<service-name>;P:<password>;;` (password from
  `RandomNumberGenerator.GetString` over a QR-safe alphabet — no `;`, `:`, `,`, `\`), render it with **QRCoder**
  (MIT, no dependencies; Desktop only) as a small control drawn dark-on-white with a quiet zone regardless of
  theme, then poll `adb mdns services` (in `CommandLogScope.Background()`) for a `_adb-tls-pairing._tcp` service
  whose instance == the service name, pair with the password on stdin, and reuse `PairAsync`'s wait-for-connect.
  Lives on the Pair tab beside the code form.
- [ ] **Enable Wi-Fi debugging from USB (B5).** A USB device card action: read the IP **first**
  (`adb -s <serial> shell ip route` → `WirelessOutputParser.ParseIpRouteSource`, falling back to
  `ip -4 addr show wlan0`), then `adb -s <serial> tcpip 5555` (prints `restarting in TCP mode port: 5555`; adbd
  restarting drops the USB link, so `shell` fails right after), then connect and remember `<ip>:5555`.

## Deferred hardening

- **`ADB_REJECT_KILL_SERVER=1`** on the local server, so a version-mismatched *remote* adb client can't kill it
  over the tunnel (dropping every wireless device). It also rejects the app's own `adb kill-server`, so
  `AdbServerService.KillAsync` (used by **Restart adb**) would first need to kill the server process directly.
  `WirelessReconnect` already reconnects saved addresses when the server or tunnel comes back, which covers the
  common case.
- **One device, several rows.** A phone can show up as its USB serial, as `ip:port`, and as
  `adb-<guid>._adb-tls-connect._tcp` at once. Grouping rows by `ro.serialno` would collapse them.

## Notes

- Everything goes through the **local** adb server — the one the tunnel exposes — so a device connected here is
  also visible to the remote host's adb. mDNS discovery and QR pairing need this machine on the phone's LAN, with
  UDP 5353 allowed for `adb.exe` through the firewall.
- `adb connect` exits 0 even when it fails; the outcome is in the message (check `already connected to` before
  `connected to`). `adb pair` failures mostly surface as a generic `error: protocol fault …`.
- An Android 11+ device's connect port changes every time Wireless debugging is toggled, so TLS connect
  addresses (and the post-pairing connect) aren't remembered — only addresses entered by hand are.
- Disconnecting an mDNS-connected device may not stick: adb's mDNS auto-connect can reconnect it.

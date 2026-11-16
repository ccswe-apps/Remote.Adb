# Wireless devices: QR pairing, Wi-Fi debugging from USB

Pillar 3's remaining work. Connecting by address, pairing with a code, disconnecting, reconnecting remembered
addresses after an adb restart, and picking devices discovered over mDNS are in (`Remote.Adb.Core/Adb/Wireless/`, the
Devices page's **Add Wireless Device** dialog, the card's **Disconnect**, and `wireless …` in the console). What's
left builds on that.

## Remaining work

- [ ] **QR pairing (B4).** Generate `WIFI:T:ADB;S:<service-name>;P:<password>;;` with the name and password drawn
  from `[A-Za-z0-9]` only (`RandomNumberGenerator.GetString`), render it with **QRCoder** (MIT; Desktop only) as a
  small fixed-size control drawn dark-on-white regardless of theme — `QRCodeData.ModuleMatrix` already includes the
  quiet zone. Don't add a second poller: watch the dialog's `WirelessDiscoveryViewModel.PairingServices` for the
  service whose instance == the service name, then call `PairAsync(service.Address, password)` (the password is just
  a pairing code). Lives on the Pair tab beside the code form; regenerate the code every ~2 minutes, pause while the
  code form is pairing, close the dialog at most once, and show the discovery-unavailable hint instead of a code
  that can't complete. Give the `adb pair` call a timeout.
- [ ] **Enable Wi-Fi debugging from USB (B5).** A USB device card action (online/authorized devices only): read the
  IP **first** — a `wlan*` route's `src` only (never mobile data/VPN), falling back to `ip -4 addr show wlan0` — then
  `adb -s <serial> tcpip 5555` (prints `restarting in TCP mode port: 5555`; adbd restarting drops the USB link, so
  `shell` fails right after), then retry connecting to (and remember) `<ip>:5555` for ~10 s.

## Deferred hardening

- **One device, several rows.** A phone can show up as its USB serial, as `ip:port`, and as
  `adb-<guid>._adb-tls-connect._tcp` at once. Grouping rows by `ro.serialno` would collapse them.

## Notes

- Everything goes through the **local** adb server — the one the tunnel exposes — so a device connected here is
  also visible to the remote host's adb. mDNS discovery and QR pairing need this machine on the phone's LAN, with
  UDP 5353 allowed for `adb.exe` through the firewall; discovery is IPv4-only.
- `adb connect` exits 0 even when it fails; the outcome is in the message (check `already connected to` before
  `connected to`). `adb pair` failures mostly surface as a generic `error: protocol fault …`. `adb mdns check` also
  exits 0 when discovery is off — it's available only if the output starts with `mdns daemon version`.
- An Android 11+ device's connect port changes every time Wireless debugging is toggled, so TLS connect
  addresses (and the post-pairing connect) aren't remembered — only addresses entered by hand and legacy `adb tcpip`
  devices (port 5555) are.
- Disconnecting an mDNS-connected device may not stick: adb's mDNS auto-connect can reconnect it.

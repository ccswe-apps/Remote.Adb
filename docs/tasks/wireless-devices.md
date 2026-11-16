# Wireless devices: Wi-Fi debugging from USB

Pillar 3's remaining work. Connecting by address, pairing with a code, disconnecting, reconnecting remembered
addresses after an adb restart, picking devices discovered over mDNS, and QR-code pairing are in (`Remote.Adb.Core/Adb/Wireless/`, the
Devices page's **Add Wireless Device** dialog, the card's **Disconnect**, and `wireless …` in the console). What's
left builds on that.

## Remaining work

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

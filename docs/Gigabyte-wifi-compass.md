# GIGABYTE Wi-Fi Compass reconstruction

Sora implements the recovered strength monitor and four-position antenna comparison using Windows Native WLAN. It also adds per-adapter access-point scanning, explicit BSSID selection, channel/band/radio details, current link rates and CSV export. GCC, its services, its DLLs and its motherboard eligibility restrictions are not runtime dependencies.

## Artifact provenance and coverage

The installed GCC directory contains the module's download descriptor and class mappings, but not the module itself. The descriptor identifies `GBT_WiFiCompass` version `24.10.30.01`, size 2,289,552 bytes. The mapping is `MB.CPNT.WiFi.Views.WiFiGroupPanel` → `GBT_WiFiCompass`, hardware identity `WIFI\WCP`. GCC's recovered downloader constructs the official package URL from that library and version:

`https://mb.download.gigabyte.com/FileList/Swhttp/LiveUpdate4/GCC/GBT_WiFiCompass/GBT_WiFiCompass_24.10.30.01.exe`

The downloaded bytes match the descriptor's exact size. The NSIS installer was extracted with portable 7-Zip, without executing or installing the vendor package. Local evidence remains under `gcc-re/wifi` and `gcc-re/evidence` (ignored investigation artifacts).

| Artifact | SHA-256 |
| --- | --- |
| Official NSIS package | `fa267c5c813c94a82e43cd6a12498b08959fd7c2767eb0791a7744d2fcec2cf6` |
| `MB.CPNT.WiFi.dll` | `917d55712250462033a87d1970b5a7ebc5eec39619675e2d55907ae61dea8a27` |
| `ManagedNativeWifi.dll` | `4f75d021e9a41a337c67bc3e4867401f9359531c695fefdd8ebb130b3e59e44d` |

REA's `inspect-managed-members` inspected the Wi-Fi assembly's full declared member inventory, CIL anchors and literal strings; `inspect-managed-native-boundaries` inspected the ManagedNativeWifi dependency's native declarations. Retained records are `gcc-re/evidence/rea-wifi-members.json` (evidence ID `ev_113240bd7590c329b6f2378d5d98e1761f4131005f03eebadce0ec244d8d6563`) and `gcc-re/evidence/rea-wifi-native-boundaries.json` (evidence ID `ev_4fde16f010e918fc5316884ee684a278b9244c215010eab9a7dd716f862cbe03`). ILSpy produced the C# reconstruction for behavioral interpretation. These are static observations, not an original vendor source release or proof of execution on every supported adapter.

The Wi-Fi module itself was separately inspected with `inspect-managed-native-boundaries`; it declares zero P/Invoke imports or native module references. Its four runtime-provided methods are delegate infrastructure. This additional record is `gcc-re/evidence/rea-wifi-module-native-boundaries.json`, ID `ev_4ce3ae0961adc541bf5d01f62f8bc0c9faaddd6edbdd50a314ce80e07fa2833a`.

## Recovered behavior

| Behavior | Evidence location in `gcc-re/wifi/decompiled` | Sora implementation |
| --- | --- | --- |
| Check strength, start/pause/stop and five-minute monitoring | `MB.CPNT.WiFi.Views/WiFiCheckStrengthPanel.cs`; background worker lines 20–76 | Explicit monitoring checkbox; unchecking pauses sampling, reset clears statistics; automatic stop after five minutes |
| 500 ms sample interval; 300 s strength / 15 s direction duration | `MB.CPNT.WiFi.Views.BackgroundWorkers/WiFiScanSignalStrengthBackgroundWorker.cs:20`, `:31` | Same nominal interval and durations; no overlapping native requests |
| Front → Left → Back → Right, each started by the user, then result | `MB.CPNT.WiFi.Views/WiFiFindDirectionPanel.cs` | Four position choices, sequential advance after measurement, cancel/retry/reset, strongest mean recommendation after all four |
| Minimum, maximum, arithmetic mean rounded to integer in GCC | `MB.CPNT.WiFi.Views/WiFiSignalStatisticsBox.cs:179`–`:211` | Minimum, maximum and mean shown with one decimal; every actual sample counts, including unchanged RSSI |
| Best direction is greatest mean, first wins ties | `MB.CPNT.WiFi.Views/WiFiFindDirectionPanel.cs:548` | Greatest measured mean wins; requires at least five fresh samples and fresh final sample |
| Quality thresholds strictly greater than −50, −60, −70, −80 dBm | `MB.CPNT.WiFi.Utilities/WiFiSignalStrengthConverter.cs:32` | Excellent, Good, Fair, Weak, Poor with the recovered boundary semantics |
| Meter maps clamped −90…−20 dBm linearly to 0…100 | `MB.CPNT.WiFi.Utilities/WiFiSignalStrengthConverter.cs:21` | Same mapping, separately labelled from driver link quality percent |
| First connected network SSID matched to first BSS with that name | `MB.CPNT.WiFi.Views.BackgroundWorkers/WiFiScanSignalStrengthBackgroundWorker.cs:89` | Explicit adapter GUID + BSSID identity avoids mixing different APs or adapters using one SSID |
| Motherboard `SystemVersion` must contain `-WCP` | `MB.CPNT.WiFi.Utilities/WiFiChecker.cs:15` | No artificial restriction; Windows WLAN determines availability |
| Managed library calls Windows WLAN API | `rea-wifi-native-boundaries.json`, all declared P/Invoke imports | Sora-owned native interop with request-local handles, native-buffer freeing and callback ownership |

The recovered utility measures signal while the user moves the antenna. No motor control, physical compass sensor, antenna phase measurement or direction-of-arrival algorithm is present in this managed implementation. Strength and directional comparison depend on received signal samples. Sora does not reproduce vendor graphics, animations, eligibility gating, or defects such as SSID-only matching and repeatedly counting the same cached BSS observation. The monitor checkbox resumes a new five-minute monitoring period while preserving accumulated statistics; GCC's paused worker retains its original elapsed-time budget.

## Native implementation and ownership

`WifiScanner.ReadAsync(CancellationToken cancellationToken = default)` provides a JSON-serializable read-only snapshot. Every call owns one `WlanOpenHandle` handle. `WlanEnumInterfaces` supplies live WLAN interfaces; disconnected adapters remain useful. `WlanQueryInterface(current_connection)` reports the connected BSSID/link rates. Signal strength comes from Windows' cached BSS entry, including for the connected network; Sora does not estimate dBm from the connection percentage.

**Sora never sends `WlanScan` (since 0.8.7).** GCC's `MB.CPNT.WiFi.dll` references only ManagedNativeWifi's `EnumerateConnectedNetworkSsids` and `EnumerateBssNetworks`: it reads Windows' existing results and never forces a scan, never queries radio state and never asks the driver for RSSI. Sora makes the corresponding calls (`WlanEnumInterfaces`, `WlanQueryInterface(current_connection)`, `WlanGetNetworkBssList`); connected RSSI is read from the same cached BSS entry as nearby networks, and radio-off comes from the BSS-list error. Earlier versions forced scans (every 3 s while the radio was off in 0.8.2-0.8.5); twice on 17 October 2026 the user's Realtek 8922AE failed its firmware reset right after (`rtwlane711` "sw ser l2 reset", Device Manager code 10). All native buffers are freed and the handle closed even after cancellation. Leaving or disposing the panel cancels pending work and stops measurement polling. It does not connect, disconnect, turn on radios, change profiles, reset drivers or modify location permissions.

The native BSS layout is verified as 360 bytes with host timestamp offset 80 and frequency offset 92; the connection layout is 604 bytes. SSIDs are read by byte length rather than NUL termination and control characters are escaped for display. Frequency conversion includes 2.4 GHz channel 14, the 6 GHz channel 2 exception, 4.9/5/6/60 GHz, and explicit unknown frequencies. The security column reports the beacon's privacy bit as open/secured; it does not claim to distinguish WPA versions.

Connected and nearby networks use the same BSS source. A BSS contributes only once per distinct host timestamp and only when within five seconds of capture; active position measurements also reject beacons timestamped before the user started that position. Re-reading an unchanged Windows cache entry does not create a new measurement. A stale target displays no live dBm. Measurements remain pinned to the same adapter and BSSID through temporary disappearance and return; changing either clears incompatible comparisons. A failed snapshot clears unavailable readings, comparison state and the export source. CSV exports include adapter, SSID, BSSID, signal, band/channel, radio and last-seen UTC; formula-like strings are escaped for spreadsheet safety.

References: [Microsoft WlanScan](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlanscan), [BSS entry layout, RSSI units and FILETIME host timestamps](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/ns-wlanapi-wlan_bss_entry), [query opcodes](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/ne-wlanapi-wlan_intf_opcode), [Windows Wi-Fi location access changes](https://learn.microsoft.com/windows/win32/nativewifi/wi-fi-access-location-changes).

## Verification and limits

`WifiChecks.Run(Action<bool,string>)` supplies 16 hardware-independent checks covering frequency exceptions, SSID byte length/control characters, recovered quality boundaries, actionable native errors, cross-adapter identity, stale samples, cached timestamp deduplication for connected and nearby targets, rejection of beacons from before a position measurement, mean-based direction selection, exact target recovery after disappearance, target-change reset and CSV identity/formula handling. The local `tools/wifi-probe` runner also verifies actual marshaled native structure sizes and offsets.

On 17 October 2026, an unrestricted read-only native scan identified one `Realtek 8922AE WiFi 7 PCI-E NIC`, disconnected. Windows returned `0x80342002` (radio powered down) for scan and nearby-BSS retrieval. Sora preserved the real adapter and reported the radio-off condition with no fabricated networks or readings. Evidence: `artifacts/wifi-live-scan.json`. Sandbox execution separately returned access denied at `WlanOpenHandle`; the unrestricted run establishes that sandbox restriction was not a successful scan or a Location-permission diagnosis.

No Wi-Fi network was connected and no radio was enabled during verification. Successful RF scanning, connected RSSI cadence, driver behavior under roaming, and the four-position physical antenna comparison remain unverified on this host while its radio is off. Windows Location access can independently block scan/BSSID/connection queries; the UI explains that only after actual access-denied responses. Static implementation coverage and deterministic checks do not establish those unavailable live-hardware outcomes.

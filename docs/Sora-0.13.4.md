# Sora 0.13.4

Improved Logitech DPI consistency when switching windows and while other hardware controllers are busy.

- Moved live DPI verification out of the shared fan/cooler polling loop. A dedicated monitor checks after foreground-window changes and at regular idle intervals, including while Sora stays in the tray.
- Retried verification when the mouse connection is busy. Hardware drift is restored to the selected stage and verified by readback. Focus changes never select a new profile or reset an intentional DPI Shift.
- Added Windows pointer-acceleration and pointer-speed information beside the DPI controls, plus a shortcut to Windows mouse settings. Acceleration and per-app sensitivity can change how 800 DPI feels without changing the mouse sensor's DPI. Sora does not automatically change global Windows pointer settings.
- Logged DPI-button transitions separately from detected hardware drift to make future reports diagnosable. No foreground-window titles or application names are logged.

Automated regression coverage includes focus changes, busy-connection retries, idle drift, intentional Shift, normal-stage restoration and monitor cancellation without hardware writes.

Exit Sora through its tray menu before installing. Normal upgrades preserve existing profiles and preferences.

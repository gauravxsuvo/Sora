# Sora regular-use checks

Current installed release: **0.10.10**, launched through the Desktop/Start menu Sora shortcut pointing to `C:\Program Files\Sora\Sora.exe`. Automatic sign-in startup is off. Keep vendor apps installed; avoid applying the same controls from two apps at once.

## Confirmed

- [x] Installation and upgrade completed; all 96 installed payload files match.
- [x] With saved synced Off, RAM, case-fan RGB, AIO pump/radiator RGB and motherboard lights are off; user confirms the window responds.
- [x] ARGB 3/RGB header and fan-control channel 4 are unused. SATA-powered case fans are excluded from motherboard speed control.

## During normal use

No special button test window is needed for these observations. Check actions you actually use; leave unused custom actions marked not applicable.

| Done | Use case | What to observe |
| --- | --- | --- |
| [ ] | Desktop and usual game | Left/right click, hold-to-aim, middle click, Back/Forward and wheel behave normally; no missed, duplicate or stuck input. |
| [ ] | DPI and assigned mouse buttons | Each configured DPI stage, G6 hold/release, tilt, G-Shift or custom action behaves as intended; regular DPI returns on release. |
| [ ] | Normal keyboard use | Typing, shortcuts, media/Fn keys and any configured remaps work without missing or repeated keys. |
| [ ] | Keyboard idle/wake and reconnect | After ordinary idle, wake promptly; check 2.4 GHz and wired reconnect separately when convenient. Keep USB available for recovery. |
| [ ] | Lighting you use | Apply a chosen effect and check RAM, motherboard, case-fan RGB and AIO RGB. Settings/status should agree with what is visible. Off has already passed at startup. |
| [ ] | AIO display during idle/game | Temperature/usage/power readings update, units and digits are sensible, no stale/frozen display. |
| [ ] | Tray Exit and manual reopen | Save work, choose Exit Sora once; check normal input and expected lighting policy, then reopen from the installed shortcut. “Rainbow when Sora closes” is enabled, so lights may turn on at Exit by design. |
| [ ] | Ordinary sleep/wake | Save work first; verify input, cooler display, lighting and profile recovery after wake. |
| [ ] | Next planned Windows restart | Save work; after sign-in, open Sora manually because automatic startup is off. Verify the same devices and saved settings. |
| [ ] | Ordinary-use observation | Record actual duration and any fault. This does not complete the separate 24-hour vendor-disabled or 48-hour post-removal gates. |

If a problem occurs, report **time, app/game, device, action, expected result and actual result**, with a screenshot if useful. If input fails, stop testing and exit Sora through its tray menu if accessible; tell me before running further live checks.

## Before vendor removal or firmware updates

- [ ] Verified recoverable C:/D: backup and separate recovery USB.
- [ ] Exact hardware revisions, supported update offers and recovery procedure.
- [ ] Remaining exclusive-controller, full-profile, failure and Windows lifecycle checks.
- [ ] Vendor profiles/reinstallers and rollback material; 24-hour vendor-disabled rehearsal.
- [ ] Review removal separately, then record 48-hour post-removal observation.

Full engineering gates: [remaining acceptance](remaining-acceptance.md). Current results: [0.10.10](Sora-0.10.10.md). Installation is complete; vendor removal and firmware installation remain HOLD.

# Logitech G502 HERO: investigation and coverage

Latest DPI correction and hardware evidence: [Sora 0.10.1](Sora-0.10.1.md).

Updated 17 October 2026 for Sora 0.10.0. The persistent continuation record is [logitech-plan.md](logitech-plan.md).

## Applying settings

Exit G HUB from its tray menu, open Sora → Logitech, edit a speed or assignment, and choose **Apply profile**. LIGHTSYNC has its own **Apply lighting**; startup/DPI indicator preferences use **Apply device settings**. Invalid values and a running G HUB block writes with visible feedback. You do not need to enable onboard memory.

G HUB's saved onboard-memory toggle is OFF on the inspected device. Its agent reports `onboardMode:false`; the hardware reports software mode 2 while that agent owns it. The agent can return the mouse to firmware mode 1 on exit. Sora reports the live controller/mode and explicitly acquires software mode on Apply. It does not reinterpret that transition as the user's saved G HUB preference.

Sora controls stay active in the tray. **Stop Sora controls** releases injected input and restores the mode, DPI, rate and button forwarding map from before takeover. Saved software profiles resume after restart/reconnect; G HUB pauses Sora. Ordinary DPI/button/lighting operation does not write onboard profile sectors. The two device preference checkboxes intentionally persist in device nonvolatile settings.

## Evidence and implementation

- REA 6.5.0 from the requested morluto/rea repository analyzed the installed `C:\Program Files\LGHUB\resources\app.asar`, version 39.1.2 / depot 876190. ASAR SHA-256: `5bcb683032410c4fc35462822751fcb1429d4da7cebfa57cda688d9468401875`. Evidence ID `ev_74b298eef8000f9c1c39efe1755301c94f2afc694600a39354d2717b38452810`. Native agent inspection followed the JavaScript work; static frontend analysis alone cannot establish complete behavior.
- G HUB's local WebSocket at `ws://localhost:9010`, subprotocol `json`, provided read-only device/profile observations. Sora's runtime uses its own C# USB implementation and does not need Logitech services or a vendor account.
- Actual wired G502 HERO **046D:C08B**, HID++ **4.2**, firmware **174.0.5**. Feature indices are discovered dynamically. Sensor range is 100–25,600 DPI in 50 DPI steps; rates are 1000/500/250/125 Hz.
- Native `Feature8110MouseButtonSpy` RTTI/vtable disassembly recovered count fn0, start fn1, stop fn2, 16-byte forwarding map get fn3/set fn4. Physical notification function0/softwareID0 carries the eleven-button big-endian bitmap. G6 is bit5, G7 bit6, G8 bit7. Sora leaves G1 native and diverts G2–G11 into an event-driven state machine and Windows SendInput. There is no global mouse hook.
- Hold G6 for the selected DPI Shift speed; release returns to the current regular stage. G7/G8 select lower/higher regular stages. G-Shift is a distinct held alternate assignment layer. Release uses the action captured on press, so releasing the modifier first cannot leave a key held.
- G9 defaults to **Next Sora control profile**. Apply at least two profiles with distinct names; G9 then cycles them in saved order and updates live DPI/report rate without writing onboard profile slots. DPI Cycle remains available as an assignment.
- Native effects use feature 8070 v3, two zones, IDs `[0,1,10,3]` (Off/Fixed/Breathing/Cycle). The wire uses an effect **index**, so Breathing ID10 becomes index2. Function14 returns the current state; fn3 writes RAM-only state with persistence byte12 zero. [Typed protocol cross-check](https://openlogi.org/hidpp/features/x8070-color-led-effects).
- Screen sampler averages a downsampled primary display at 10 frames/s, with whole/left/right area choices. Audio visualizer follows the default speaker endpoint's peak level; it does not record a microphone or implement G HUB's frequency analysis. Both stream the recovered RAM-only fixed-color command and require Sora running.
- DPI Always On was recovered by tracing G HUB's own enable/disable changes: 1300 fn6 reads NV flags, fn7 writes flag2 (always) or4 (automatic). After that write, G HUB refreshes the DPI indicator with fn5 using the current display slot. Sora also sends fn5 on software DPI changes and Shift transitions. Fn4's getter is a different byte layout and does not echo the pattern on firmware174.0.5; it must not be replayed as a setter. Automatic display timeout and indicator ownership are managed by Sora.
- Startup effect was recovered by tracing G HUB off/on: 8070 fn4 with capability `00 01`, fn5 with byte2=1 enabled/2 disabled. Both settings are re-read and compared before mutation, verified afterward, and restored independently on failure.
- Artwork is imported from the user's local G HUB depot: front/side PNGs and actual zone/assignment coordinates from manifest/metadata. LED colors sit behind the front PNG's transparent cutouts. Cache: `data/logitech/artwork`. Sora contains no generated mouse picture or embedded vendor executable/firmware/JavaScript.

## Recovery and persistence

Each process-local HID connection reserves a separate software ID, serializes requests, and correlates exact replies across both HID collections. A timeout faults the connection; button notifications use a bounded channel and fail closed on overflow. Control service cleanup cancels macros and releases held input before restoring the original forwarding map/mode/sensitivity. Original ownership records survive interrupted sessions and are retried on reconnect. Reactive lighting keeps a separate identity-checked original-state record and opens a fresh connection for restoration. If G HUB takes ownership, Sora avoids overwriting its new settings.

Control profiles are versioned, size-bounded Sora JSON: stages, Shift speed, rate, both assignment layers and up to32 macros. Import stages edits and requires Apply. Keyboard shortcuts, mouse/scroll commands, media keys, application launch, HTTP(S) links and chord macros are supported. Macro modes: no repeat, repeat while held, toggle, and press/hold/release sequence. Each step taps a chord after its delay; explicit key-down/key-up event editing and vendor action plugins are not implemented. Primary click stays native for recovery. G HUB profile import and automatic foreground-application switching are not implemented.

## Validation

- Debug build: zero warnings/errors. `./test.ps1 -NoRestore`: **35 groups pass**, including captured protocol/CRC fixtures, zero-nibble HID++ error routing, DPI Shift hold/release, unsorted DPI up/down stages, G9 profile-cycle dispatch, simultaneous G-Shift resolution, correct key release, repeated macro cancellation and nonblocking sequence cleanup.
- Live bounded runtime check passed (`logitech-re/runtime-full-verification.json`): acquire software mode; apply1600 DPI/500Hz; toggle both device preferences and restore them; stream screen and audio lighting; restore original mode/DPI/rate/button map and both lighting states. Earlier failures remain as investigation evidence.
- Previous native effect test passed Fixed/Breathing/Cycle/Off on each zone with readback and restoration. Five onboard profile sectors independently pass CRC-CCITT.
- UI checks stage/discard changes without hardware writes: invalid DPI/Shift/stages/RGB/hex, drafts across zones/tabs, alternate assignment layer, primary-click preservation, conflict blocking and cached front/side artwork.
- Accepted commands/readback do not prove physical USB polling accuracy, measured animation timing, perceived brightness, every G HUB animation phase, or actual physical G6 hold/release delivery. Physical button timing and injected actions still need user exercise on the new release. Screen/audio output is implemented independently and is not claimed to match G HUB algorithms exactly.

## Remaining coverage

Onboard profile editing/store/rename, automatic game/application profiles, vendor Actions integrations, G HUB macro import/event recording, frequency-based audio effects, cross-device phase synchronization, Lua/cloud/community features, firmware flashing and factory reset are unavailable. Other Logitech device families and wireless receivers are unsupported. This is a substantially expanded G502 integration, not complete reverse engineering of every G HUB feature.

Investigation artifacts remain under `logitech-re` (ignored): `rea-application.json`, `agent-query.json`, `spy-vtable.json`, `spy-disassembly.json`, `dpi-led-hid-capture.json`, `startup-effect-hid-capture.json`, `runtime-full-verification.json`. Primary references: [Logitech HID++ index](https://github.com/Logitech/cpg-docs/blob/master/hidpp20/README.rst), [libratbag implementation](https://github.com/libratbag/libratbag/blob/master/src/hidpp20.c). Sora uses its own implementation.

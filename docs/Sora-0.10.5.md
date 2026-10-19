# Sora 0.10.5

## Mouse control recovery

- Serializes HID++ transactions between Sora's mouse connections. The Logitech editor now borrows the controller connection instead of opening another connection during normal control. Host timer, startup and window refreshes cannot run the same background tick concurrently.
- Checks hardware DPI, firmware/software mode and button forwarding every two seconds, including while running only in the tray. Lost control or a failed connection cancels the old runtime and schedules a new connection with the saved profile.
- DPI button presses always write and read back the requested speed. Cached DPI can no longer suppress a boundary press or a request to restore a stage.
- The last regular stage is saved in `logitech/control-runtime.json` and restored on startup/resume. Holding Shift never becomes the saved startup DPI. Applying a profile explicitly selects its chosen default stage.
- Failed hardware reads remove the live DPI marker instead of falling back to the editor's selected speed. Inactive controls can be reapplied even when the saved profile has no edits.
- Indicator failures are reported separately and retried; they do not immediately stop the button runtime. A failed transport is recovered by the independent connection checks.

## Linked mouse lighting

- Moves **Sync DPI and logo lighting** to the top of LIGHTSYNC.
- Saves that preference across window closure and app restarts.
- Disables individual zone selection, including preview hit targets, while linked.
- Both draft zones and both preview lights update together as effect, colour and brightness are edited. Apply lighting saves and sends the look using the existing device protocol.

Release build compilation and publication completed. Physical restart, button and LED behaviour still requires confirmation on the mouse. Motherboard/RAM lighting effect encoding is unchanged.

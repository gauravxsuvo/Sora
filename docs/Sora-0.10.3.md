# Sora 0.10.3

## Saved settings and startup

- All releases now share `%LOCALAPPDATA%\Sora\data`. Updating Sora, starting in the tray, or launching a development build uses the same saved choices.
- The first launch imports the newest valid copy of each legacy settings file, including device profiles, original-state backups and artwork. Existing shared files are never overwritten; legacy folders are retained. `migration-v1.json` records the source of every imported file.
- Lighting edits save immediately and apply automatically about 900 ms after editing stops. Invalid colour input is not applied. Apply lighting remains available to apply immediately; Discard edits cancels a pending edit before application.
- Saved lighting is reapplied on startup and after resume, including tray launches, when Apply when Sora starts is enabled (the default). Conflicts and unavailable devices remain pending with automatic retries rather than silently giving up. No hardware flash programming was added.
- The lighting editor refreshes from the completed startup scan instead of retaining its initial placeholder. Pending restoration is displayed separately from live state.
- Firmware lighting effects applied to the Logitech mouse are now saved with the mouse identity and restored on startup/resume. Reactive lighting and enabled DPI/button profiles retain their existing automatic restoration.
- Existing fan startup preferences and the cooler's Turn on when Sora starts preference remain effective. Keyboard settings applied to the keyboard continue to be stored by its existing device protocol.

## Limits

- Restoring a previously selected look requires that it was saved; an old editor placeholder cannot reconstruct an unsaved colour or effect. Choose it once in this version and it will be remembered.
- Other RGB applications must release the hardware before Sora can restore lighting. Pending restores retry automatically.
- RAM cannot retain every effect across power loss without Sora running; this release reapplies the saved look instead of writing controller flash.

Build: Release compilation and publication. Physical startup/resume appearance has not been verified on this release.

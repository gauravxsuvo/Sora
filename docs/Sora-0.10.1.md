# Sora 0.10.1 — live DPI and G502 indicators

The DPI outline now tracks the hardware reading when DPI Up/Down or held DPI Shift changes the sensor. Shift moves the outline to the yellow marker; release returns it to the current regular speed. Background polling refreshes the hardware reading while Sora controls are active. Live updates preserve staged edits and the saved default stage.

Selecting the saved default speed is now an actionable edit when the live mouse is at another speed. Previously a profile could still display its saved 800 DPI selection while the buttons had moved the sensor to 1600, and selecting 800 again could leave Apply disabled. DPI Down also stops correctly at the minimum of an unsorted speed list.

The G502 has separate controllers for sensor DPI (2201), indicator stage/mode (1300), and RGB zone colour (8070). The software button path previously updated only the sensor. Sora now acquires indicator software control and sends the stage command on DPI presses and Shift transitions, including a press at the maximum/minimum speed. Automatic lighting hides after approximately two seconds; held Shift keeps the display illuminated, and Always On remains illuminated. Runtime stage changes do not write NV preferences or onboard profiles.

The current profile's regular speeds and Shift speed share five ordered display slots. If there are six distinct speeds, the upper two share slot 5. The RGB colour/effect for the DPI zone remains the user's LIGHTSYNC choice.

## Evidence

- A G HUB capture during the user's physical presses showed 400/800/1600/4000 DPI requests and separate stage 1/2/3/4 indicator requests. Fixture: `tests/Sora.Checks/fixtures/logitech-g502-dpi-indicator.json`; full local capture: `logitech-re/physical-dpi-indicator-capture.json`.
- G HUB's 800 DPI request contains sensor bytes `00 03 20`; Sora sends the same value. Direct Sora HID setter/readback returned 800, and the original DPI/indicator mode were restored. Both RGB zones and the Always On preference were unchanged. Evidence: `logitech-re/indicator-live-verification-20261018.json`.
- Native Feature1300LEDControl disassembly establishes the separate getter/setter formats. Firmware 174.0.5 echoes On/Off but does not echo the selected LED pattern. Stage commands are checked against captured G HUB bytes; readback confirms mode, not perceived illumination. For an original illuminated indicator without a reported pattern, restoration uses the saved firmware stage as a fallback.
- The WPF render check verifies marker pixels at 800, 1600, held Shift 4000 and released 1600 while preserving the default stage. The release's 16 editor checks pass with zero hardware writes, including reselecting a saved speed while live DPI differs. The 35 core check groups pass.

## G HUB independence

Mouse controls use Sora's direct Windows HID implementation. G HUB services, APIs and DLLs are not runtime dependencies. Profiles, assignments, macros and cached artwork are kept in Sora's own `data/logitech` directory. The packaged cache includes front/side images and metadata, so uninstalling G HUB does not remove those images. Controls remain usable through the selectors if optional artwork is absent. Sora must keep running for software assignments and DPI Shift.

## Run this release

Quit the older Sora instance and exit G HUB from its tray menu. Launch `E:\Sora\dist\Sora-0.10.1\Sora.exe` with administrator access, open Logitech, and apply the profile. The release contains a copy of the current Debug installation's data, including its Logitech profiles and lighting backup. Gigabyte lighting implementation files were not edited.

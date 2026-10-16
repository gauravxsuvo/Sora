# Kreo keyboard controls in Sora 0.3.1

Connect the keyboard over USB in wired mode and close Kreo KB Hub. Open **Kreo keyboard** in Sora; the app reads the keyboard automatically. Use **Connect / refresh** after reconnecting it.

## Lighting

- Choose one of the 18 named effects, **Off**, or **Custom per-key**.
- Adjust brightness/speed where supported. Each effect remembers its own settings.
- For a custom effect color, choose **Single hex color**, select a palette swatch, and enter a hex value such as `#12ABEF`. The effect stays selected. **Multicolor** uses all palette colors. Effects with fixed colors disable these controls.
- To paint individual keys, select a key on the board, enter a hex value under **Per-key RGB**, then choose **Paint selected key**. **Fill whole keyboard** paints all keys. Painting selects custom per-key mode.

Edits are staged. The bottom bar shows pending changes and stays visible. **Apply to keyboard** saves them to the device; **Discard edits** cancels them.

## Key assignment

Click the physical source key on the board, open **Key assignment**, choose the desired action, and select **Use this assignment**. The source → target preview and key legend show the staged change. Then use **Apply to keyboard**.

Targets are standard key actions, even if those keys were previously remapped. Media actions and Disabled are included. **Reset selected key** restores its standard action; **Undo all key edits** restores the last read state. Fn keeps its layer function.

After applying, click the test field and press the physical key. It shows the action Windows receives. Only events in that focused field are displayed; no key events are saved.

## Profiles and backups

The **Profiles** tab saves/loads editor setups, including effect palettes. Loading stages edits; Apply saves them to the keyboard. A backup is saved before each Apply in `data\keyboard` beside Sora.exe. Existing 0.3 profile files import with their omitted block tails and effect palette supplied from the connected keyboard.

## Validation and scope

On the real Swarm 75, a physical F12 press arrived as F11 after remapping, followed by restoration. Static/Breathing custom effect palettes and per-key colors passed device readback. 18 automated checks and the WPF editor interaction checks passed. Hive 75/98 definitions remain unverified on physical Hive hardware.

Configuration requires wired USB. Macros, Fn-layer editing, wireless/Bluetooth configuration, audio-reactive streaming, debounce/sleep controls, vendor profile-file compatibility, firmware updates and factory reset are not included.

The LD360 tab still uses independent CPU sensors through LibreHardwareMonitor and PawnIO. Launch Sora.exe normally and accept its administrator prompt for CPU register access. Keep PawnIO installed.

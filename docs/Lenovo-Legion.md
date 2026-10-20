# Lenovo Legion controls

Sora now includes an experimental Windows backend and a Lenovo Legion page. It is implemented from pinned open source references and tested with simulated transports. No physical Legion hardware has been tested in this workspace, so this is a preview for evaluation, not a claim of verified support across every Legion.

## Available controls

| Feature | Current behavior |
| --- | --- |
| Identification | Lenovo manufacturer plus Legion marketing identity or one of 48 laptop machine types from the pinned Toolkit catalog; Go and tower identities are excluded |
| Power presets | Quiet, Balanced and Performance through the verified Gamezone WMI interface; Performance requires detected AC power |
| Battery | Normal, Conservation and Rapid charge through the installed Lenovo Energy Management driver |
| Graphics mode | Supported firmware Hybrid, Integrated-only, Automatic and Discrete settings through the default Gamezone path; active display routing can require a restart |
| Four-zone lighting | Static, Breathing, Color cycle, directional waves and Off on the explicitly recognized 33-byte ITE interfaces |
| Spectrum lighting | Compatibility exchange, stored profile selection and brightness 0–9 on compatible 960-byte ITE interfaces; readback and session restoration |

Firmware capabilities determine which controls appear enabled. A missing charging driver does not disable unrelated power or lighting controls. Sora does not install Lenovo drivers or require the Vantage UI to operate; particular features still depend on installed Lenovo drivers and firmware interfaces.

## Use

Open Sora normally on a detected Legion laptop and select **Lenovo Legion**. Refresh reads the available features. Select a setting and press its Apply button. No Legion settings are automatically applied at startup or resumed from saved profiles.

Close competing controllers before applying changes. Sora checks for known Lenovo/RGB controller processes and blocks its write actions while they are active; it does not stop services or uninstall software. The page refreshes every five seconds while visible, including firmware mode changes made outside Sora. Reads and transactions are serialized inside Sora.

Power and charging changes, GPU configuration and Spectrum settings save a recovery record before the first mutation, then verify configuration readback. Failed transactions attempt to restore the previous configuration; a failed recovery is reported as unknown device state. Cancellation or closing Sora cannot interrupt a transaction's recovery: shutdown waits for it to finish.

**Restore original** uses the initial setting from the current Sora session, including when the window has been closed to the tray and reopened. Recovery JSON is retained under the Sora data directory's `lenovo-legion/recovery` folder. It is diagnostic evidence; Sora does not automatically replay recovery files after a crash or reboot.

Four-zone controllers do not expose an original-color getter in the examined implementations. The first color command has no original-state restore promise. If a later command fails, Sora can attempt to resend its previous known session command. A successful native send is described as a command sent, not verified color readback.

GPU readback describes the firmware's configured mode, not proof that a GPU is powered off or that the active display path has already changed. Sora does not kill GPU applications, eject devices, alter BIOS passwords or restart Windows automatically.

## Not included in this preview

Custom fan curves and custom CPU/GPU power limits have source-reference codecs/research but are not exposed as live controls. Spectrum per-key effect editing, Corsair/iCUE-controlled lighting, BIOS UMA switching and experimental alternative GPU capability backends are also not implemented. Legion Go, Legion towers, LOQ, IdeaPad and ThinkPad are separate workstreams.

For an interface report, use `--lenovo-legion-diagnose --output PATH` or the standalone collector described in [diagnostics](Lenovo-Legion-diagnostics.md). These diagnostics do not apply configuration. They can be used by future testers; owning a laptop is not required to continue source-led development.

## Verification

The repository checks cover packet formats, foreign-device rejection, missing interfaces, source model identities, charging request order, backups, readback mismatches, failures after mutation, failed recovery, controller conflicts, AC restrictions, cancellation and shutdown. `--legion-ui-check --output PATH` renders the page with synthetic hardware and checks independent feature enablement without device access.

Protocol/source details: [protocol map](Lenovo-Legion-protocol-map.md), [source inventory](lenovo-legion-sources.json), [plan](../lenovo-legion-plan.md).

# Logitech G HUB integration plan

Requested 17 October 2026. Target: installed G HUB 39.1.2, not an assumed generic Logitech protocol.

## Investigation and acceptance gates

1. Inventory the installed ASAR, native agent, device manifests, local configuration schema and connected HID interfaces. Record file versions and SHA-256 provenance. Use the existing REA 6.5.0 CLI for static JavaScript analysis, then focused extraction to avoid mistaking a partial parse for complete coverage.
2. Recover the feature matrix from shipped routes, translations, manifests and RPC messages: device home, sensitivity/DPI and report rate, assignments/G-Shift, macros, LIGHTSYNC, game/application profiles, persistent profile, onboard memory, power/battery, audio/microphone/equalizer/BLUE VO!CE, cameras, wheels/pedals, integrations, settings and update/firmware flows. Mark each implemented, source-derived, unverified or unavailable.
3. Identify this PC's actual Logitech devices and transport. Trace UI → configuration/RPC → native agent → HID++. Separate independent device control from functions that require G HUB. Never infer a receiver's paired device from its USB product ID alone.
4. Implement a typed Logitech core with capability discovery, bounded request/response matching, error decoding, profile persistence and explicit unsupported states. Restrict hardware writes to recovered commands and discovered capabilities; retain originals and verify readback. Do not flash firmware, factory-reset or rewrite onboard storage during investigation.
5. Add a native Sora Logitech page (Ctrl+9): device picker/home, large device preview, per-device profile selector and capability-aware sections following G HUB's workflows. Use proper loading, empty, disconnected, conflict, error, dirty, applying and readback states. Keep original Sora pages working.
6. Implement supported controls in evidence-backed increments: sensitivity/report rate first where supported; assignments, lighting and battery next; then additional device classes only with their exact schemas/protocols. Profile and macro editors must validate references, retain drafts, and distinguish software execution from onboard support.
7. Verify protocol parsers against captured/recovered fixtures, run Sora's existing checks, compile WPF, inspect rendered screenshots and perform bounded live reads. Hardware mutation tests require a recoverable original, readback and restoration. Publish a coverage report without claiming that static extraction proves physical behavior.

## Deliverables

- `logitech-re/`: local analysis, inventory and evidence; no proprietary G HUB code embedded in Sora. As requested for 0.9.1, import the user's installed device image and zone metadata into the local artwork cache.
- `docs/Logitech-GHUB.md`: evidence, architecture, feature matrix, supported devices and remaining work.
- Independent core and native UI integrated into the existing desktop application.
- Build/check results and UI screenshots. Release packaging only after validation; existing release data must be preserved.

## Completion definition

Every discovered feature has a recorded disposition. An enabled hardware control must have an implemented transport, validated input and verified response semantics. Unknown features stay explicitly unavailable; decorative editors do not count as working device support. Complete parity across all G HUB hardware cannot be asserted from one installed build and this PC's devices.

## First implementation progress

- Completed: REA ASAR analysis, artifact provenance, literal-route inventory, actual G502 HERO detection, feature discovery, DPI/rate reads and writes, independent lighting, sensitivity presets/import/export, original backup/staging, native UI, onboard directory/slot inspection and default/G-Shift binding decoding.
- Verified: all four lighting modes plus sensitivity apply/readback/restoration; 34 automated check groups; eight live UI editor checks. See the report for exact scope.
- Pending: host assignments/G-Shift runtime, macro engine, automatic application profiles, onboard editor/writes, streaming LIGHTSYNC, wireless support, other hardware families, vendor/cloud integrations and lifecycle reapplication. These are follow-up phases, not completed functionality.

## 0.10.0 continuation

The first implementation log above is historical. Host assignments, G-Shift, chord macros, software reactive lighting and lifecycle handling are now implemented. See [the active continuation plan](logitech-plan.md) for completed work, hardware checks, release status and physical-test limitations.

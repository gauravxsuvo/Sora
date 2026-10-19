# This PC: confirmed connections

Updated 18 October 2026 from the user's description and attached Sora lighting screenshot. These are user-confirmed connections; software detection and physical effect acceptance are separate checks.

## Motherboard lighting outputs

| Sora label | Controller lighting zone | Connected hardware | Physical output checks |
| --- | --- | --- | --- |
| ARGB header 1 | 5 | Case-fan RGB | Test case-fan lighting. |
| ARGB header 2 | 6 | AIO pump RGB and AIO radiator-fan RGB | Test both pump and radiator-fan lighting together on this output. |
| ARGB header 3 | 7 | Nothing connected | No attached lights to visually qualify; record as not applicable, not a physical pass. |
| RGB header | 4 | Nothing connected | No attached lights to visually qualify; record as not applicable, not a physical pass. |

The controller zone numbers come from the existing Z890 GAMING X WIFI7 catalog in `src/Sora.Core/GigabyteRgb.cs`. Connection information belongs to this PC; it does not change the controller's supported-zone catalog or establish lighting write/readback behavior.

## Fan-speed connections are separate

The RGB/ARGB outputs above identify lighting wiring. They do not identify the separate motherboard fan-speed/power connections.

- Existing qualification records identify fan-control header 0 as the AIO pump and header 5 as radiator fans.
- **Fan-control channel 4 is unused on this PC, per the user's clarification.** The software's internal index 4 is labelled CPU OPT; it is not a request to locate a physical connector numbered 4. Exclude this unused channel from connected-fan curve, RPM-response and calibration acceptance.
- **Case fans are non-PWM and powered by SATA; their RGB connects to motherboard ARGB header 1.** This latest clarification supersedes the earlier uncertain/3-pin answer. No motherboard fan-speed connection for these case fans has been established.
- ARGB carries lighting control. A SATA power connection and an ARGB lighting connection alone do not establish a software speed-control path. Do not assume the fans accept motherboard PWM/DC curves; a separate supported controller/control connection would need identifying before claiming adjustable speed.
- The permanent installation removed the unused channel-4 saved profile and labelled it “Unused (CPU OPT)”; motherboard fan auto-apply remains off. Earlier channel-4 observations are historical, not evidence of an attached fan.
- This update does not establish exact motherboard/card/peripheral revisions.

The initial connection-recording update changed no hardware/settings. Later permanent installation updated saved fan preferences as described above, without requesting fan configuration writes. On 0.10.10 startup, the user physically confirmed RAM, motherboard, case-fan and AIO lighting off; this does not qualify other effects or empty outputs.

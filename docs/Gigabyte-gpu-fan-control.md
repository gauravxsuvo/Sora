# GPU fans

NVIDIA fan support depends on the installed driver and exposed hardware capabilities. Monitoring, software duty control, and native RPM programming are different capabilities.

Native RPM programming is restricted to qualified card interfaces. Unsupported cards must not receive another model's native commands. Zero RPM can be normal when firmware fan-stop behavior is enabled.

Profiles require Apply. Sora saves original settings before taking control and verifies supported writes. Stop or Restore releases the corresponding control mode. Software curves require the running application; native firmware behavior depends on the chosen mode.

Use manufacturer guidance for thermal limits and hardware maintenance. See the [user guide](user-guide.md).

# Storage health

Sora reads device-dependent storage identity, firmware version, volumes, temperature, SMART information, endurance, and TRIM support. Missing or unsupported readings are reported explicitly.

Health readings do not establish that firmware is current or that a backup exists. Sora does not erase drives or flash their firmware. Exported diagnostics may include device identifiers; review them before sharing.

Firmware maintenance requires the exact drive model and revision, a compatible official utility, a recoverable backup, and manufacturer instructions. See [hardware maintenance](GCC-hardware-tools.md).

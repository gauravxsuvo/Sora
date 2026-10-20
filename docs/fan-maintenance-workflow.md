# Fan control and maintenance

1. Read the current fan state and identify its physical header or GPU.
2. Save original state before applying a supported profile.
3. Close competing hardware controllers before changing settings.
4. Verify readback and observe actual fan response.
5. Restore original control before maintenance that changes the driver or device interface.

Never infer physical fan behavior from configuration readback alone. Sora does not change pump wiring, install drivers, or perform firmware maintenance.

For support limits, see [motherboard fans](Gigabyte-fan-control.md) and [GPU fans](Gigabyte-gpu-fan-control.md).

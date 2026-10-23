# Motherboard fans

Motherboard fan programming is restricted to the Gigabyte Z890 GAMING X WIFI7 and its supported controller interface. Other board models do not gain controls through a brand-name match.

Read current configuration before editing a header. Identify the physical fan or pump connection before changing its operating mode. Connection names and types are descriptive preferences and do not program a header by themselves.

Click empty space on the curve to add a point. Click a point or its table row to select it, then use **Remove point**, right-click the point and choose **Remove point**, or press **Delete** while the chart has focus. The chart and table share the same selection. Keep at least two points; the final 100% point cannot be removed, but can be dragged to change the full-speed temperature.

Apply saves original state, programs the selected supported configuration, and verifies readback. Restore returns supported original settings. A fixed-speed pump should retain its intended operating mode.

“Give Sora priority over fan helper services” is enabled by default. When applying a curve, Sora automatically pauses the supported DeepCool `FanControl` and Gigabyte `EasyTuneEngineService` helpers. Saved profiles use the same handoff at startup and resume. While an applied profile is active, Sora checks every five seconds for helper restarts and reclaims control. Enable Settings → Start at sign-in to launch Sora automatically with Windows.

The handoff captures all six headers and firmware stop settings, saves a durable recovery record, releases hardware handles, stops the helpers, then restores and verifies the captured settings before applying the requested curve. It requires Sora’s standalone firmware fan support; install it using the button on this page if needed. A stopped helper can restore a stale curve, so simply ignoring its process or repeatedly overwriting its registers is insufficient.

Applied curves are kept after exit by default and paused helpers stay stopped, preventing a vendor restart from replacing the curve. Enabling restore-on-exit restores the original fan settings and resumes previously running helpers. Windows startup types remain unchanged. Failed recovery retains `fans-priority-session.json` and prevents service restart until recovery verifies. Other fan-control applications still need to be closed; RGB, display, and unrelated services are left alone. Uncheck the priority option to return to blocking Apply while a competing helper runs.

The separately installed sensor driver and administrator access may be required. See [maintenance](fan-maintenance-workflow.md).

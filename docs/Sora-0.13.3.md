# Sora 0.13.3

Rebuilt the traffic-light window controls with heavier filled symbols: a clearer close cross, a solid minimize bar, and paired triangles for maximize and restore.

The dot and symbol now share a single rendering center. This removes the independent layout rounding that could shift the previous symbol relative to its circle at Windows display scales. The buttons keep their native close, minimize, maximize, restore, keyboard-focus and window-drag behavior.

Validated rendered pixels at 100%, 125%, 150%, 175% and 200% display scaling, including both maximize and restore states. Normal upgrades preserve existing settings. Exit Sora through its tray menu before installing.

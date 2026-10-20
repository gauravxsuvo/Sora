# Sora 0.13.0-beta.3

Adds in-app beta updates. Beta builds default to the **Beta** channel; stable builds default to **Stable**. Choose a channel under Updates, then use **Check now**, **Download update**, and **Install and restart**. Switching channels never installs an older version. Stable users receive betas only after explicitly choosing Beta.

**Beta.1 and beta.2 users must install this version manually once.** Those builds cannot read beta update feeds. Download `Sora-Setup-0.13.0-beta.3-x64.exe`, exit Sora through its tray menu and run Setup. Saved settings are retained. Future beta releases can be installed from this build's Updates page.

Both channels verify the publisher's signed metadata, installer size and SHA-256. Beta versions compare numerically, so beta.10 follows beta.9. Each channel retains separate replay protection, last-check time, skipped version and notification state. Switching channels invalidates any pending download. The helper re-verifies the chosen channel and full installed prerelease version before and after installation.

Automatic checks provide notifications; installation remains an explicit action. Sora releases hardware ownership before Setup and reopens afterward. BIOS, drivers and peripheral firmware are not updated by this flow.

Requires Windows x64 and the .NET 8 Windows Desktop Runtime (x64). Legion firmware compatibility remains experimental and has not been tested on physical Legion hardware. The EXE is not Authenticode signed; see the included download warning guidance.

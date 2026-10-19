using System;
using System.Diagnostics;
using Sora.Core;

namespace Sora.App;

internal static class MaintenanceLinks
{
    internal static void Open(string url)
    {
        if (!HardwareMaintenance.IsSupportUrl(url)) throw new ArgumentException("This is not a reviewed maintenance page.", nameof(url));
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}

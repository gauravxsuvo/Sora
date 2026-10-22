using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Sora.App;

internal static class WindowsPointerSettings
{
    internal static string Description()
    {
        var mouse = new int[3]; var speed = new int[1];
        if (!SystemParametersInfo(3, 0, mouse, 0) || !SystemParametersInfo(0x70, 0, speed, 0))
            return "Windows pointer settings unavailable. Mouse DPI is separate from app sensitivity.";
        return $"Windows pointer acceleration: {(mouse[2] == 0 ? "Off" : "On")} · Pointer speed: {speed[0]}/20. " +
            (mouse[2] == 0 ? "Apps can still apply their own sensitivity." : "Acceleration can feel different in apps that use raw input.");
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint action, uint parameter, [In, Out] int[] values, uint flags);
}

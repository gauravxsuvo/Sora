using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sora.App;

// --measure <file>: records memory and idle CPU after 20 s in the launched state, then again 10 s after hiding to the tray.
internal static class MemoryReport
{
    public static async Task RunAsync(App app, string output)
    {
        await Task.Delay(TimeSpan.FromSeconds(20));
        var launchedVisible = app.WindowOpen;
        var launched = await SampleAsync();
        app.CloseWindowToTray();
        await Task.Delay(TimeSpan.FromSeconds(10));
        var tray = await SampleAsync();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { launchedVisible, launched, tray }, new JsonSerializerOptions { WriteIndented = true }));
        await app.ExitAsync();
    }

    private static async Task<object> SampleAsync()
    {
        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        await Task.Delay(TimeSpan.FromSeconds(5));
        process.Refresh();
        var gc = GC.GetGCMemoryInfo();
        return new
        {
            workingSetMB = Math.Round(process.WorkingSet64 / 1048576.0, 1),
            privateMB = Math.Round(process.PrivateMemorySize64 / 1048576.0, 1),
            gcHeapMB = Math.Round(gc.HeapSizeBytes / 1048576.0, 1),
            gcCommittedMB = Math.Round(gc.TotalCommittedBytes / 1048576.0, 1),
            threads = process.Threads.Count,
            assemblies = AppDomain.CurrentDomain.GetAssemblies().Length,
            cpuMsPer5s = Math.Round((process.TotalProcessorTime - cpuBefore).TotalMilliseconds),
        };
    }
}

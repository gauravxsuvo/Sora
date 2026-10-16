using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace Sora.App;

// "Start with Windows": a Task Scheduler logon task that runs Sora elevated (no UAC prompt at sign-in) straight into the tray.
internal static class StartupTask
{
    private const string Name = "Sora";
    public const string Argument = "--startup";

    // Path of the exe the registered task starts, or null when no task exists.
    public static string? RegisteredPath()
    {
        var (code, output) = Run("/Query", "/TN", Name, "/XML", "ONE");
        if (code != 0) return null;
        var match = Regex.Match(output, "<Command>(.*?)</Command>", RegexOptions.Singleline);
        return match.Success ? SecurityElement.FromString("<c>" + match.Groups[1].Value + "</c>")?.Text?.Trim('"') : "";
    }

    public static bool IsEnabled => Run("/Query", "/TN", Name).Code == 0;

    public static void Enable()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Sora's program path is unknown.");
        var user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Starts Sora in the notification area when you sign in.</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId><Delay>PT5S</Delay></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author"><Exec><Command>"{SecurityElement.Escape(exe)}"</Command><Arguments>{Argument}</Arguments><WorkingDirectory>{SecurityElement.Escape(Path.GetDirectoryName(exe)!)}</WorkingDirectory></Exec></Actions>
            </Task>
            """;
        var file = Path.Combine(Path.GetTempPath(), "sora-startup-task.xml");
        File.WriteAllText(file, xml, Encoding.Unicode);
        try
        {
            var (code, output) = Run("/Create", "/TN", Name, "/XML", file, "/F");
            if (code != 0) throw new IOException("Windows did not create the startup task: " + output.Trim());
        }
        finally { File.Delete(file); }
    }

    public static void Disable()
    {
        if (!IsEnabled) return;
        var (code, output) = Run("/Delete", "/TN", Name, "/F");
        if (code != 0) throw new IOException("Windows did not remove the startup task: " + output.Trim());
    }

    // After an update the task may still point at an older Sora.exe. Returns what happened, for the log.
    public static string RepointIfMoved()
    {
        var registered = RegisteredPath();
        if (registered == null)
        {
            // The XML query can fail where a plain existence query works; re-register rather than leave a stale path.
            if (Run("/Query", "/TN", Name).Code != 0) return "not enabled";
            Enable();
            return "re-registered for " + Environment.ProcessPath + " (path query failed)";
        }
        if (string.Equals(registered, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return "up to date";
        Enable();
        var now = RegisteredPath();
        return string.Equals(now, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase) ? "moved from " + registered : "update did not take effect; still " + now;
    }

    private static (int Code, string Output) Run(params string[] arguments)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("schtasks.exe could not start.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}

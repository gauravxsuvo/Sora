using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace Sora.App;

// "Start with Windows": a Task Scheduler logon task that runs Sora elevated (no UAC prompt at sign-in) straight into the tray.
// The task is written only when the user explicitly turns it on. Normal launches never migrate it. Up to 0.8.6 a
// failing "schtasks /XML" read made every launch re-create it, and Defender flagged the new unsigned Sora.exe for rewriting an
// elevated logon task (Behavior:Win32/Persistence.A!ml) and removed it. Releases now share the stable path dist\Sora-current
// (a junction build.ps1 moves), so updates no longer change the task at all.
internal static class StartupTask
{
    private const string Name = "Sora";
    public const string Argument = "--startup";
    private const string Marker = "Sora recovery v2";

    /// <summary>The task's program and description, read with the Task Scheduler API (read-only); null when no task exists.</summary>
    public static StartupRegistration? Registered()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new IOException("Task Scheduler is unavailable.");
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        dynamic task;
        try { task = service.GetFolder("\\").GetTask(Name); }
        // No such task: .NET surfaces 0x80070002 as FileNotFoundException, a COMException or wrapped, depending on the binder.
        catch (Exception error) when (error.HResult == unchecked((int)0x80070002) || error.InnerException?.HResult == unchecked((int)0x80070002)) { return null; }
        var definition = task.Definition;
        string description = definition.RegistrationInfo.Description ?? "";
        if ((int)definition.Actions.Count != 1) throw new IOException("The Sora startup task must have exactly one action; inspect it in Task Scheduler.");
        foreach (var action in definition.Actions)
            if ((int)action.Type == 0) return new StartupRegistration(((string)action.Path ?? "").Trim('"'), description, (bool)task.Enabled);
        throw new IOException("The Sora startup task has an unexpected action; inspect it in Task Scheduler.");
    }

    /// <summary>The path the task should start: the stable dist\Sora-current junction when it leads to this Sora, else this exe.</summary>
    public static string TargetPath
    {
        get
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Sora's program path is unknown.");
            var directory = Path.GetDirectoryName(exe)!;
            var current = Path.Combine(Path.GetDirectoryName(directory) ?? directory, "Sora-current", Path.GetFileName(exe));
            return SameProgram(current, exe) ? current : exe;
        }
    }

    /// <summary>True when both paths reach the same Sora.exe, following a junction or symbolic link on the folder.</summary>
    private static bool SameProgram(string a, string b)
    {
        try
        {
            if (!File.Exists(a) || !File.Exists(b)) return false;
            static string Real(string path)
            {
                var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!);
                var target = directory.ResolveLinkTarget(true)?.FullName ?? directory.FullName;
                return Path.Combine(target, Path.GetFileName(path));
            }
            return string.Equals(Real(a), Real(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    public static bool IsEnabled => Registered()?.Enabled == true;

    public static void Enable()
    {
        var exe = TargetPath;
        var user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>{Marker}: starts in the tray after sign-in and resumes saved hardware settings after wake.</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger>
                <EventTrigger><Enabled>true</Enabled><Subscription>&lt;QueryList&gt;&lt;Query Id="0" Path="System"&gt;&lt;Select Path="System"&gt;*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and EventID=1]]&lt;/Select&gt;&lt;/Query&gt;&lt;/QueryList&gt;</Subscription><Delay>PT8S</Delay></EventTrigger>
              </Triggers>
              <Principals><Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
                <RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure>
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
        if (Registered() == null) return;
        var (code, output) = Run("/Delete", "/TN", Name, "/F");
        if (code != 0) throw new IOException("Windows did not remove the startup task: " + output.Trim());
    }

    public static StartupInspection InspectForLaunch() => StartupRouting.Inspect(Registered, Environment.ProcessPath ?? "", SameProgram);

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

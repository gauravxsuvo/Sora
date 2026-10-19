using System.Diagnostics;
using Microsoft.Win32;
using Sora.Core.Updates;
using Sora.Update.Windows;

namespace Sora.UpdateRunner;

internal static class UninstallCleanup
{
    // Only invoked explicitly by the installed uninstaller, never by app startup or an update check.
    internal static int Run(bool purge)
    {
        try
        {
            var expected = Path.Combine(UpdatePaths.InstallDirectory, "UpdateRunner");
            if (!Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\').Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Uninstall cleanup must run from the installed Sora directory.");
            UpdatePaths.RejectLinks(expected);
            using var gate = UpdatePaths.EnterGate();
            foreach (var process in Process.GetProcessesByName("Sora"))
                using (process) throw new IOException("Exit Sora from its tray menu before uninstalling. Hardware cleanup must finish first.");
            foreach (var process in Process.GetProcessesByName("Sora.UpdateRunner"))
                using (process) if (process.Id != Environment.ProcessId) throw new IOException("Another Sora update or cleanup is running. Wait for it to finish.");
            var profiles = Profiles();
            var directories = new List<(string Parent, string Name)>();
            if (purge)
            {
                foreach (var profile in profiles)
                {
                    directories.Add((Path.Combine(profile, "AppData", "Local"), "Sora"));
                    directories.Add((Path.Combine(profile, "AppData", "Roaming"), "Sora"));
                    directories.Add((Path.Combine(profile, "Downloads"), "Sora updates"));
                    directories.Add((Path.Combine(profile, "AppData", "Local", "Temp", ".net"), "Sora"));
                }
                directories.Add((Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Sora-Updates"));
                directories.Add((Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Sora"));
                directories.Add((UpdatePaths.InstallDirectory, "data"));
                foreach (var (parent, name) in directories) OwnedDirectoryRemoval.Validate(parent, name);
            }
            RemoveOwnedTask();
            foreach (var profile in profiles)
            {
                foreach (var relative in new[] { @"Desktop\Sora.lnk", @"OneDrive\Desktop\Sora.lnk", @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Sora.lnk", @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Sora\Sora.lnk" })
                    RemoveOwnedShortcut(Path.Combine(profile, relative));
                var menuDirectory = Path.Combine(profile, @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Sora");
                if (Directory.Exists(menuDirectory) && !Directory.EnumerateFileSystemEntries(menuDirectory).Any()) Directory.Delete(menuDirectory);
            }
            RemoveOwnedShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Sora.lnk"));
            RemoveOwnedShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Sora.lnk"));
            RemoveOwnedShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Sora.lnk"));
            RemoveOwnedShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Sora.lnk"));
            var assets = Path.Combine(UpdatePaths.InstallDirectory, "Assets");
            UpdatePaths.RejectLinks(assets);
            if (Directory.Exists(assets))
                foreach (var icon in Directory.EnumerateFiles(assets, "sora-brand-*.ico"))
                {
                    UpdatePaths.RejectLinks(icon);
                    var version = Path.GetFileNameWithoutExtension(icon)["sora-brand-".Length..];
                    if (Version.TryParse(version, out _)) File.Delete(icon);
                }
            foreach (var (parent, name) in directories) OwnedDirectoryRemoval.Remove(parent, name);
            RefreshShell();
            return 0;
        }
        catch (Exception error)
        {
            MessageBox(IntPtr.Zero, "Sora could not finish uninstall preparation. No installed program files have been removed.\n\n" + error.Message,
                "Sora uninstall", 0x10);
            return 1;
        }
    }

    private static string[] Profiles()
    {
        using var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList") ?? throw new IOException("Windows profiles could not be inspected.");
        var profiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sid in root.GetSubKeyNames().Where(sid => sid.StartsWith("S-1-5-21-", StringComparison.Ordinal) || sid.StartsWith("S-1-12-1-", StringComparison.Ordinal)))
        {
            using var key = root.OpenSubKey(sid);
            if (key?.GetValue("ProfileImagePath") is not string path) throw new IOException("A Windows profile path could not be read.");
            var resolved = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
            if (Directory.Exists(resolved)) { UpdatePaths.RejectLinks(resolved); profiles.Add(resolved); }
        }
        return profiles.ToArray();
    }

    internal static int RepairShortcuts()
    {
        try
        {
            var expected = Path.Combine(UpdatePaths.InstallDirectory, "UpdateRunner");
            if (!Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\').Equals(expected, StringComparison.OrdinalIgnoreCase)) return 1;
            var version = FileVersionInfo.GetVersionInfo(UpdatePaths.InstalledExe);
            var icon = Path.Combine(UpdatePaths.InstallDirectory, "Assets", $"sora-brand-{version.FileMajorPart}.{version.FileMinorPart}.{version.FileBuildPart}.ico");
            UpdatePaths.RejectLinks(icon);
            if (!File.Exists(icon)) return 1;
            var paths = Profiles().SelectMany(profile => new[] { @"Desktop\Sora.lnk", @"OneDrive\Desktop\Sora.lnk", @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Sora.lnk", @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Sora\Sora.lnk" }.Select(relative => Path.Combine(profile, relative)))
                .Concat(new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), @"Sora\Sora.lnk"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Sora.lnk") });
            foreach (var path in paths)
            {
                if (!File.Exists(path)) continue;
                UpdatePaths.RejectLinks(path);
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
                dynamic link = shell.CreateShortcut(path);
                if (!SameExe((string)link.TargetPath)) continue;
                link.IconLocation = icon + ",0"; link.Save();
            }
            RefreshShell();
            return 0;
        }
        catch { return 1; }
    }

    private static void RemoveOwnedTask()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new IOException("Task Scheduler is unavailable.");
        dynamic scheduler = Activator.CreateInstance(type)!; scheduler.Connect();
        dynamic folder = scheduler.GetFolder(@"\");
        dynamic tasks = folder.GetTasks(1);
        for (var i = 1; i <= (int)tasks.Count; i++)
        {
            dynamic task = tasks.Item(i);
            if (!string.Equals((string)task.Name, "Sora", StringComparison.OrdinalIgnoreCase)) continue;
            dynamic actions = task.Definition.Actions;
            if ((int)actions.Count != 1 || !SameExe((string)actions.Item(1).Path))
                throw new IOException("A task named Sora belongs to another installation. Inspect it before uninstalling.");
            if ((int)task.State == 4) throw new IOException("The Sora startup task is running. Exit Sora before uninstalling.");
            folder.DeleteTask("Sora", 0);
        }
    }

    private static bool SameExe(string value) => Path.GetFullPath(value.Trim('"')).Equals(UpdatePaths.InstalledExe, StringComparison.OrdinalIgnoreCase);
    private static void RemoveOwnedShortcut(string path)
    {
        if (!File.Exists(path)) return;
        UpdatePaths.RejectLinks(path);
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic link = shell.CreateShortcut(path);
        if (SameExe((string)link.TargetPath)) File.Delete(path);
    }
    internal static void RefreshShell() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    [System.Runtime.InteropServices.DllImport("shell32.dll")] private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);
}

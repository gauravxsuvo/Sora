using System;
using System.IO;
using System.Threading;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Sora.Update.Windows;

internal static class UpdatePaths
{
    internal const string GateName = @"Global\Sora.ApplicationUpdate.v1";
    internal static string InstallDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Sora");
    internal static string InstalledExe => Path.Combine(InstallDirectory, "Sora.exe");
    internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Sora-Updates");
    private static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier SystemUser = new(WellKnownSidType.LocalSystemSid, null);

    internal static void RejectLinks(string path)
    {
        for (var item = new DirectoryInfo(Path.GetFullPath(path)); item != null; item = item.Parent)
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("An update path contains a link.");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("An update file is a link.");
    }

    internal static void RequirePrivateDirectory(string path)
    {
        RejectLinks(path);
        var acl = new DirectoryInfo(path).GetAccessControl();
        var owner = acl.GetOwner(typeof(SecurityIdentifier));
        if (owner == null || (!owner.Equals(Admins) && !owner.Equals(SystemUser))) throw new IOException("Update staging must be owned by Administrators or SYSTEM.");
        const FileSystemRights writing = FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
            FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & writing) != 0 &&
                !rule.IdentityReference.Equals(Admins) && !rule.IdentityReference.Equals(SystemUser))
                throw new IOException("Update staging is writable by a non-administrator.");
    }

    internal static string Stage(string id, bool create = false)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new IOException("Invalid update transaction ID.");
        RejectLinks(Root);
        if (!Directory.Exists(Root))
        {
            var acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(true, false); acl.SetOwner(Admins);
            foreach (var sid in new[] { Admins, SystemUser })
                acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(Root).Create(acl);
        }
        RequirePrivateDirectory(Root);
        var path = Path.Combine(Root, id);
        if (create)
        {
            if (Directory.Exists(path)) throw new IOException("Update transaction already exists.");
            Directory.CreateDirectory(path);
        }
        RequirePrivateDirectory(path);
        return path;
    }

    internal sealed class GateLease(Mutex mutex) : IDisposable
    {
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }
    internal static GateLease EnterGate()
    {
        var gate = new Mutex(false, GateName);
        try
        {
            bool acquired;
            try { acquired = gate.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("Sora is installing an update. Wait for installation to finish, then open Sora again.");
            return new GateLease(gate);
        }
        catch { gate.Dispose(); throw; }
    }
}

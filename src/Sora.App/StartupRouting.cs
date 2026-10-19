using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Sora.App;

internal sealed record StartupRegistration(string Path, string Description, bool Enabled);
internal sealed record StartupInspection(bool Readable, StartupRegistration? Task, string Message);

// Normal launches only inspect registration. Changing copies, enabling, disabling and updating
// an older task layout require the user's explicit Start at sign-in action.
internal static class StartupRouting
{
    internal static StartupInspection Inspect(Func<StartupRegistration?> read, string executable, Func<string, string, bool> sameProgram)
    {
        try
        {
            var task = read();
            if (task == null) return new(true, null, "Not enabled.");
            if (!task.Enabled) return new(true, task, "Disabled in Task Scheduler. Turn on Start at sign-in to enable this copy.");
            if (sameProgram(task.Path, executable)) return new(true, task, "Starts this copy at sign-in. Task left unchanged.");
            return new(true, task, "Sign-in uses another copy: " + task.Path + ". Turn Start at sign-in off, then on, to use this copy.");
        }
        catch (Exception error) when (error is IOException or COMException or UnauthorizedAccessException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return new(false, null, "Startup task could not be read; left unchanged. " + error.Message);
        }
    }

    internal static bool ParseExplicitAction(string? action) => action switch
    {
        "on" => true,
        "off" => false,
        _ => throw new ArgumentException("Use --startup-task on or --startup-task off.")
    };
}

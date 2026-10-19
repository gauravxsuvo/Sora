using Sora.App;
using System.Runtime.InteropServices;

internal static class StartupRoutingChecks
{
    internal static void Run(Action<bool, string> require)
    {
        const string installed = @"C:\Program Files\Sora\Sora.exe";
        const string development = @"E:\Sora\dist\Sora-current\Sora.exe";
        var checks = 0;
        // Inspect has no mutation callback or scheduler dependency. Both directions preserve
        // registration, including disabled/absent and older-description tasks.
        foreach (var current in new[] { installed, development })
        foreach (var registered in new[] { installed, development })
        foreach (var enabled in new[] { false, true })
        foreach (var description in new[] { "Sora recovery v2", "older task layout" })
        {
            var task = new StartupRegistration(registered, description, enabled);
            var reads = 0;
            var result = StartupRouting.Inspect(() => { reads++; return task; }, current, (a, b) => a == b);
            require(result.Readable && ReferenceEquals(result.Task, task) && reads == 1, "registration snapshot is preserved");
            require(!enabled ? result.Message.StartsWith("Disabled") : current == registered ? result.Message.StartsWith("Starts this copy") : result.Message.Contains("another copy"), "routing is described without migrating");
            checks++;
        }
        foreach (var current in new[] { installed, development })
        {
            var absent = StartupRouting.Inspect(() => null, current, (_, _) => throw new Exception("absent comparison"));
            require(absent.Readable && absent.Task == null && absent.Message == "Not enabled.", "absent stays absent");
            checks++;
        }
        foreach (var error in new Exception[] { new IOException("read failure"), new UnauthorizedAccessException("denied"), new COMException("scheduler unavailable"), new Microsoft.CSharp.RuntimeBinder.RuntimeBinderException("unexpected COM shape") })
        {
            var result = StartupRouting.Inspect(() => throw error, installed, (_, _) => throw new Exception("unreadable comparison"));
            require(!result.Readable && result.Task == null && result.Message.Contains("left unchanged"), "failed read stays unchanged");
            checks++;
        }
        var stable = new StartupRegistration(development, "older task layout", true);
        require(StartupRouting.Inspect(() => stable, @"E:\Sora\dist\Sora-0.10.7\Sora.exe", (_, _) => true).Message.StartsWith("Starts this copy"), "resolved stable junction is current without rewriting legacy task");
        checks++;
        require(StartupRouting.ParseExplicitAction("on") && !StartupRouting.ParseExplicitAction("off"), "only explicit on/off accepted");
        checks++;
        foreach (var action in new string?[] { null, "", "ON", "yes", "of", "--startup", "on off" })
        {
            try { StartupRouting.ParseExplicitAction(action); throw new Exception("invalid startup action accepted"); }
            catch (ArgumentException) { checks++; }
        }
        require(checks == 31, "all 31 startup routing cases ran");
    }
}

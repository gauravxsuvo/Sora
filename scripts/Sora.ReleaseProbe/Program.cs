using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

// Runs the existing client's real check logic with isolated settings. Does not start App or hardware.
if (args.Length != 4) throw new ArgumentException("Usage: INSTALLED_SORA_DLL NEW_SETTINGS_DIRECTORY EXPECTED_RELEASE OUTPUT_JSON");
var dll = Path.GetFullPath(args[0]);
var settings = Path.GetFullPath(args[1]);
if (Directory.Exists(settings)) throw new IOException("Choose a new isolated settings directory.");
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var dependency = Path.Combine(Path.GetDirectoryName(dll)!, name.Name + ".dll");
    return File.Exists(dependency) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(dependency) : null;
};
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
assembly.GetType("Sora.App.SoraData", true)!.GetMethod("UseIsolatedPreviewDirectory", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [settings, false]);
var type = assembly.GetType("Sora.App.ApplicationUpdateService", true)!;
using var service = (IDisposable)Activator.CreateInstance(type, nonPublic: true)!;
await (Task)type.GetMethod("CheckAsync")!.Invoke(service, [false])!;
object? Get(string name) => type.GetProperty(name)!.GetValue(service);
var release = Get("Release") ?? throw new IOException((string?)Get("Status"));
var offered = (string)release.GetType().GetProperty("Version")!.GetValue(release)!;
var installed = (Version)Get("InstalledVersion")!;
var available = (bool)Get("Available")!;
if (offered != args[2] || available != (Version.Parse(offered) > installed)) throw new IOException("Unexpected release or update availability.");
var result = new { passed = true, installedVersion = installed.ToString(), offeredVersion = offered, available, status = Get("Status"), release, isolatedSettings = settings, hardwareStarted = false };
using var output = new FileStream(args[3], FileMode.CreateNew, FileAccess.Write);
JsonSerializer.Serialize(output, result, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine($"Actual client {installed} accepted live release {offered}; update available: {available}. Hardware was not started.");

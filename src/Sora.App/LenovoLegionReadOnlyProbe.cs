using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Sora.Core;

namespace Sora.App;

internal static class LenovoLegionReadOnlyProbe
{
    internal static async Task<bool> RunAsync(string output)
    {
        // Before SoraHost construction: no saved profiles, ownership changes or controller startup.
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var report = await Task.Run(LenovoLegionProbe.Capture);
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        await JsonSerializer.SerializeAsync(file, report, options);
        return report.Completed;
    }
}

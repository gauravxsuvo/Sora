using System.Text.Json;

namespace Sora.Core.Updates;

public static class BundledUpdatePolicy
{
    public static UpdatePolicy Load()
    {
        using var stream = typeof(BundledUpdatePolicy).Assembly.GetManifestResourceStream("Sora.UpdatePolicy")
            ?? throw new InvalidDataException("Missing bundled update policy.");
        return JsonSerializer.Deserialize<UpdatePolicy>(stream, UpdatePolicy.Json) ?? throw new InvalidDataException("Invalid bundled update policy.");
    }
}

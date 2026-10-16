using System.Buffers.Binary;
using System.Text.Json;

namespace Sora.Core;

public sealed record KreoKey(string Label, double X, double Y, double Width, double Height, int VirtualKey, int Index, int Type = 2, uint PresetAction = 0);
public sealed record KreoEffect(byte Id, string Name, bool Speed, bool Brightness, bool Color) { public override string ToString() => Name; }
public sealed record KreoModel(string Name, string[] Identities, KreoKey[] Keys, KreoEffect[] Effects)
{
    public override string ToString() => Name;
    public static IReadOnlyList<KreoModel> All { get; } = Load();
    private static KreoModel[] Load()
    {
        using var stream = typeof(KreoModel).Assembly.GetManifestResourceStream("Sora.Core.KreoModels.json")!;
        return JsonSerializer.Deserialize<KreoModel[]>(stream)!;
    }
}

public sealed record KreoState(string Identity, byte[] Lighting, byte[] Keys, byte[] Colors, byte[]? EffectPalette = null)
{
    public KreoState Copy() => new(Identity, (byte[])Lighting.Clone(), (byte[])Keys.Clone(), (byte[])Colors.Clone(), EffectPalette == null ? null : (byte[])EffectPalette.Clone());
    public void Validate()
    {
        if (!KreoModel.All.Any(m => m.Identities.Contains(Identity)) || Lighting?.Length != 128 || Keys?.Length != 512 || Colors?.Length != 378 || EffectPalette?.Length != 512)
            throw new InvalidDataException("This is not a supported Sora keyboard profile.");
        if (Lighting[126] != 0x5a || Lighting[127] != 0xa5)
            throw new InvalidDataException("Keyboard settings are missing the KB Hub validity marker.");
    }
}

public static class KreoProtocol
{
    public const int ReportLength = 520;
    public static byte[] Request(byte command, byte selector, int length, int page = 0, int pages = 1)
    {
        if (length is < 1 or > 512 || page < 0 || page >= pages || pages > 255) throw new ArgumentOutOfRangeException(nameof(length));
        if (command is not (0x82 or 0x83 or 0x84 or 0x86 or 0x8a or 0x03 or 0x04 or 0x06 or 0x0a)) throw new ArgumentException("Unsupported Kreo command.", nameof(command));
        var packet = new byte[ReportLength];
        packet[0] = 6; packet[1] = command; packet[2] = selector;
        packet[4] = (byte)pages; packet[5] = (byte)page;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6), (ushort)length);
        return packet;
    }

    public static byte[] Payload(byte[] request, byte[] response)
    {
        if (request.Length != ReportLength || response.Length != ReportLength || !response.AsSpan(0, 8).SequenceEqual(request.AsSpan(0, 8)))
            throw new IOException("Unexpected Kreo response header: " + Convert.ToHexString(response.AsSpan(0, Math.Min(16, response.Length))));
        return response.AsSpan(8, BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(6))).ToArray();
    }

    public static void SetLighting(KreoState state, KreoModel model, byte effect, int brightness, int speed, int color)
    {
        state.Validate();
        if (effect is not (0 or 21) && !model.Effects.Any(e => e.Id == effect) || brightness is < 0 or > 4 || speed is < 0 or > 4 || color is < 0 or > 7)
            throw new ArgumentOutOfRangeException(nameof(effect));
        // These boards use the FillCfg representation at 0x49e8a0.
        state.Lighting[9] = (byte)(effect == 21 ? 1 : 0);
        state.Lighting[10] = effect;
        if (effect is > 0 and <= 18)
        {
            state.Lighting[56 + 2 * effect] = (byte)speed;
            state.Lighting[57 + 2 * effect] = (byte)((brightness << 4) | color);
        }
    }

    public static void Paint(KreoState state, int index, byte red, byte green, byte blue)
    {
        state.Validate();
        if (index is < 0 or >= 126) throw new ArgumentOutOfRangeException(nameof(index));
        state.Colors[index] = red; state.Colors[index + 126] = green; state.Colors[index + 252] = blue;
    }

    public static void Remap(KreoState state, int source, byte[] action)
    {
        state.Validate();
        if (source is < 0 or >= 126 || action.Length != 4) throw new ArgumentOutOfRangeException(nameof(source));
        action.CopyTo(state.Keys, source * 4);
        CompleteBlock(state.Keys);
    }

    public static void CompleteBlock(byte[] block)
    {
        if (block.Length != 512) throw new ArgumentException("Keyboard configuration blocks must contain 512 bytes.");
        block[508] = block[509] = 0; block[510] = 0x5a; block[511] = 0xa5;
    }

    public static byte[] ParseHex(string text)
    {
        var hex = text.Trim();
        if (hex.StartsWith('#')) hex = hex[1..];
        if (hex.Length != 6) throw new FormatException("Enter six hex digits, for example #769BFF.");
        return Convert.FromHexString(hex);
    }

    public static void SetEffectColor(KreoState state, KreoModel model, byte effect, int slot, byte[] rgb)
    {
        state.Validate();
        if (!model.Effects.Any(e => e.Id == effect && e.Color) || slot is < 0 or > 6 || rgb.Length != 3) throw new ArgumentException("This effect does not support a custom color.");
        rgb.CopyTo(state.EffectPalette!, effect * 21 + slot * 3);
        CompleteBlock(state.EffectPalette!);
        var packed = 57 + 2 * effect;
        state.Lighting[packed] = (byte)((state.Lighting[packed] & 0xf0) | slot);
    }

    public static KreoState Import(KreoState profile, KreoState current)
    {
        if (profile.Identity != current.Identity) throw new InvalidDataException("This profile belongs to a different keyboard model.");
        // v0.3 profiles omitted the final eight key-map bytes and the effect palette.
        var keys = profile.Keys;
        if (keys?.Length == 504) { keys = (byte[])current.Keys.Clone(); profile.Keys.CopyTo(keys, 0); }
        var upgraded = profile with { Keys = keys!, EffectPalette = profile.EffectPalette ?? (byte[])current.EffectPalette!.Clone() };
        upgraded.Validate();
        if (!upgraded.Keys.SequenceEqual(current.Keys)) CompleteBlock(upgraded.Keys);
        return upgraded;
    }
}

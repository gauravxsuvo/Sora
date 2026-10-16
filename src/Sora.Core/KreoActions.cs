namespace Sora.Core;

public sealed record KreoAction(string Label, byte[] Bytes) { public override string ToString() => Label; }

public static class KreoActions
{
    public static byte[] ForKey(KreoKey key)
    {
        if (key.Type == 2) return ForVirtualKey(key.VirtualKey);
        if (key.Type == 9)
        {
            var action = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(action, key.PresetAction); return action;
        }
        throw new ArgumentException("That key action type cannot be assigned yet.");
    }
    public static byte[] ForVirtualKey(int vk)
    {
        if (vk is >= 0x41 and <= 0x5a) return [0, 0, 0, (byte)(vk - 0x41 + 4)];
        if (vk is >= 0x31 and <= 0x39) return [0, 0, 0, (byte)(vk - 0x31 + 0x1e)];
        if (vk is >= 0x70 and <= 0x7b) return [0, 0, 0, (byte)(vk - 0x70 + 0x3a)];
        if (vk is >= 0x61 and <= 0x69) return [0, 0, 0, (byte)(vk - 0x61 + 0x59)];
        var modifier = vk switch { 0xa2 => 1, 0xa0 => 2, 0xa4 => 4, 0x5b => 8, 0xa3 => 16, 0xa1 => 32, 0xa5 => 64, 0x5c => 128, _ => 0 };
        if (modifier != 0) return [0, (byte)modifier, 0, 0];
        if (vk == 0xfa) return [0x0d, 0, 0, 0];
        if (vk == 0x8f || vk == 0xad) return [2, 0, 0, 0xe2];
        var usage = vk switch
        {
            0x1b => 0x29, 0x30 => 0x27, 8 => 0x2a, 9 => 0x2b, 0x0d => 0x28, 0x20 => 0x2c,
            0xbd => 0x2d, 0xbb => 0x2e, 0xdb => 0x2f, 0xdd => 0x30, 0xdc => 0x31,
            0xba => 0x33, 0xde => 0x34, 0xc0 => 0x35, 0xbc => 0x36, 0xbe => 0x37, 0xbf => 0x38,
            0x14 => 0x39, 0x2c => 0x46, 0x91 => 0x47, 0x13 => 0x48, 0x2d => 0x49, 0x24 => 0x4a,
            0x21 => 0x4b, 0x2e => 0x4c, 0x23 => 0x4d, 0x22 => 0x4e, 0x27 => 0x4f, 0x25 => 0x50, 0x28 => 0x51, 0x26 => 0x52,
            0x90 => 0x53, 0x6f => 0x54, 0x6a => 0x55, 0x6d => 0x56, 0x6b => 0x57, 0xfd => 0x58, 0x60 => 0x62, 0x6e => 0x63,
            _ => throw new ArgumentException("That key cannot be assigned yet.")
        };
        return [0, 0, 0, (byte)usage];
    }

    public static IReadOnlyList<KreoAction> ForModel(KreoModel model) => model.Keys.Where(k => k.VirtualKey != 0xfa)
        .Select(k => new KreoAction(k.Label, ForKey(k)))
        .Concat(new[] { new KreoAction("Play / pause", [2, 0, 0, 0xcd]), new KreoAction("Previous track", [2, 0, 0, 0xb6]), new KreoAction("Next track", [2, 0, 0, 0xb5]), new KreoAction("Volume up", [2, 0, 0, 0xe9]), new KreoAction("Volume down", [2, 0, 0, 0xea]), new KreoAction("Disabled", [0, 0, 0, 0]) }).ToArray();

    public static string Describe(KreoModel model, byte[] action) => ForModel(model).FirstOrDefault(a => a.Bytes.SequenceEqual(action))?.Label ?? "Custom action";
}

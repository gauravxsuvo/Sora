namespace Sora.Core;

// Shared by the physical USB keyboard and failure-injection checks.
internal static class KreoKeyboardTransaction
{
    internal static KreoState Apply(string identity, KreoModel model, KreoState expected, KreoState desired,
        Func<KreoState> read, Action<byte, byte[]> write, Action<KreoState> backup, Action settle)
    {
        desired.Validate();
        expected.Validate();
        if (desired.Identity != identity) throw new IOException("This profile belongs to another keyboard model.");
        var before = read();
        before.Validate();
        if (expected.Identity != identity || !Same(before, expected))
            throw new IOException("Keyboard settings changed since they were read. Read the keyboard again before applying edits.");
        foreach (var key in model.Keys.Where(k => k.VirtualKey == 0xfa))
            if (!before.Keys.AsSpan(key.Index * 4, 4).SequenceEqual(desired.Keys.AsSpan(key.Index * 4, 4)))
                throw new IOException("This profile changes the Fn key. Sora preserves its existing layer function.");
        desired = desired.Copy();
        if (!before.Keys.AsSpan(0, 508).SequenceEqual(desired.Keys.AsSpan(0, 508))) KreoProtocol.CompleteBlock(desired.Keys);
        else before.Keys.CopyTo(desired.Keys, 0);
        if (!before.EffectPalette!.AsSpan(0, 508).SequenceEqual(desired.EffectPalette!.AsSpan(0, 508))) KreoProtocol.CompleteBlock(desired.EffectPalette!);
        else before.EffectPalette!.CopyTo(desired.EffectPalette!, 0);
        backup(before.Copy());
        var changes = new List<(byte Command, byte[] Before, byte[] Desired)>();
        void Plan(byte command, byte[] oldBytes, byte[] newBytes)
        {
            if (!oldBytes.SequenceEqual(newBytes)) changes.Add((command, oldBytes, newBytes));
        }
        Plan(0x0a, before.EffectPalette!, desired.EffectPalette!);
        Plan(6, before.Colors, desired.Colors);
        Plan(3, before.Keys, desired.Keys);
        Plan(4, before.Lighting, desired.Lighting);
        var attempted = new List<(byte Command, byte[] Before, byte[] Desired)>();
        try
        {
            foreach (var change in changes)
            {
                // A setter can modify the device before it reports a transport error.
                attempted.Add(change);
                write(change.Command, change.Desired);
            }
            settle();
            var actual = read();
            actual.Validate();
            if (!Same(actual, desired)) throw new IOException("The keyboard did not confirm every setting.");
            return actual;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            var restoreErrors = new List<Exception>();
            foreach (var change in attempted.AsEnumerable().Reverse())
                try { write(change.Command, change.Before); }
                catch (Exception restore) when (restore is not OutOfMemoryException) { restoreErrors.Add(restore); }
            try
            {
                settle();
                if (!Same(read(), before)) throw new IOException("The restored keyboard state did not match the original.");
            }
            catch (Exception restore) when (restore is not OutOfMemoryException) { restoreErrors.Add(restore); }
            if (restoreErrors.Count > 0)
                throw new IOException("Keyboard apply failed and full restoration could not be verified. The pre-change backup is retained; reconnect and restore it. " + error.Message,
                    new AggregateException(new[] { error }.Concat(restoreErrors)));
            throw new IOException("Keyboard apply failed. The previous settings were restored and read back. " + error.Message, error);
        }
    }

    private static bool Same(KreoState a, KreoState b) => a.Identity == b.Identity &&
        a.Lighting.SequenceEqual(b.Lighting) && a.Keys.SequenceEqual(b.Keys) &&
        a.Colors.SequenceEqual(b.Colors) && a.EffectPalette!.SequenceEqual(b.EffectPalette!);
}

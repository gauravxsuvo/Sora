namespace Sora.Core;

/// <summary>Leave unchanged standard buttons on the mouse's native input path.</summary>
public static class LogitechButtonRouting
{
    private static readonly string[] Standard = ["Native", "MouseRight", "MouseMiddle", "MouseBack", "MouseForward"];

    public static LogitechAction Resolve(LogitechControlProfile profile, int button, bool shifted)
    {
        RequireLayers(profile);
        if (button is < 0 or >= 11) throw new ArgumentOutOfRangeException(nameof(button));
        var action = shifted ? profile.ShiftButtons[button] : profile.Buttons[button];
        return action.Kind == "Default" ? profile.Buttons[button] : action;
    }

    public static bool IsNative(LogitechControlProfile profile, int button)
    {
        RequireLayers(profile);
        return button >= 0 && button < Standard.Length &&
            Resolve(profile, button, false).Kind == Standard[button] && Resolve(profile, button, true).Kind == Standard[button];
    }

    public static byte[] NativeMap(LogitechControlProfile profile)
    {
        RequireLayers(profile);
        if (profile.Buttons[0].Kind != "Native" || profile.ShiftButtons[0].Kind != "Native")
            throw new InvalidDataException("Primary click must remain native.");
        var map = new byte[16];
        for (var button = 0; button < Standard.Length; button++) if (IsNative(profile, button)) map[button] = (byte)(button + 1);
        return map;
    }

    public static LogitechAction RuntimeAction(LogitechControlProfile profile, int button, bool shifted) =>
        IsNative(profile, button) ? new("Native", Resolve(profile, button, shifted).Name) : Resolve(profile, button, shifted);

    public static bool CanChangeWhileHeld(LogitechControlProfile before, LogitechControlProfile after, ushort bitmap)
    {
        var oldMap = NativeMap(before); var newMap = NativeMap(after);
        for (var button = 0; button < 11; button++)
            if (oldMap[button] != newMap[button] && (bitmap & (1 << button)) != 0) return false;
        return true;
    }

    private static void RequireLayers(LogitechControlProfile profile)
    {
        if (profile.Buttons is not { Length: 11 } || profile.ShiftButtons is not { Length: 11 } ||
            profile.Buttons.Any(a => a == null) || profile.ShiftButtons.Any(a => a == null))
            throw new InvalidDataException("The G502 needs eleven assignments per layer.");
    }
}

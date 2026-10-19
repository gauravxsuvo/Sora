using System.Text.Json;

namespace Sora.Core;

public sealed record LogitechAction(string Kind, string Name, int[]? Keys = null, string? Target = null);
public sealed record LogitechMacroStep(string Phase, int DelayMs, int[] Keys);
public sealed record LogitechMacro(string Id, string Name, string Mode, LogitechMacroStep[] Steps);
public sealed record LogitechControlProfile(int Version, string Name, ushort ProductId, int[] Stages, int DefaultStage, int ShiftDpi,
    int ReportRate, LogitechAction[] Buttons, LogitechAction[] ShiftButtons, LogitechMacro[] Macros)
{
    public static LogitechControlProfile Create(int dpi = 800, int rate = 1000) => new(1, "Desktop: Default", 0xc08b,
        new[] { 400, 800, 1600 }.Append(dpi).Distinct().Order().Take(5).ToArray(), Array.IndexOf(new[] { 400, 800, 1600 }.Append(dpi).Distinct().Order().Take(5).ToArray(), dpi), 4000, rate,
        [new("Native", "Primary click"), new("MouseRight", "Secondary click"), new("MouseMiddle", "Middle click"), new("MouseBack", "Back"), new("MouseForward", "Forward"), new("DpiShift", "DPI Shift"), new("DpiDown", "DPI Down"), new("DpiUp", "DPI Up"), new("ProfileNext", "Next Sora control profile"), new("ScrollRight", "Scroll right"), new("ScrollLeft", "Scroll left")],
        Enumerable.Range(0, 11).Select(i => new LogitechAction(i == 0 ? "Native" : "Default", i == 0 ? "Primary click" : "Same as default")).ToArray(), []);

    public void Validate(LogitechSnapshot snapshot)
    {
        if (Version != 1 || ProductId != snapshot.ProductId || string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || snapshot.Sensors.Length != 1 || Stages == null || Stages.Length is < 1 or > 5 || Stages.Distinct().Count() != Stages.Length || DefaultStage < 0 || DefaultStage >= Stages.Length || Stages.Any(d => !snapshot.Sensors[0].Accepts(d)) || !snapshot.Sensors[0].Accepts(ShiftDpi) || !snapshot.ReportRates.Contains(ReportRate)) throw new InvalidDataException("Choose one to five supported DPI speeds, a current stage, a valid Shift speed and report rate.");
        if (Buttons?.Length != 11 || ShiftButtons?.Length != 11 || Buttons.Any(a => a == null) || ShiftButtons.Any(a => a == null) || Buttons[0].Kind != "Native" || ShiftButtons[0].Kind != "Native") throw new InvalidDataException("The G502 needs eleven assignments per layer; primary click remains native for recovery.");
        if (Macros == null || Macros.Length > 32 || Macros.Any(m => m == null) || Macros.Select(m => m.Id).Distinct().Count() != Macros.Length) throw new InvalidDataException("Invalid macro library.");
        foreach (var macro in Macros)
        {
            if (string.IsNullOrWhiteSpace(macro.Id) || string.IsNullOrWhiteSpace(macro.Name) || macro.Name.Length > 80 || macro.Mode is not ("NoRepeat" or "WhileHeld" or "Toggle" or "Sequence") || macro.Steps == null || macro.Steps.Length is < 1 or > 128 || macro.Steps.Any(s => s == null || s.Phase is not ("Press" or "Hold" or "Release") || s.DelayMs is < 0 or > 10000 || !ValidKeys(s.Keys)) || macro.Steps.Sum(s => (long)s.DelayMs) > 60000) throw new InvalidDataException("A macro needs 1–128 valid chord steps, delays of 0–10000 ms, and at most 60 seconds per sequence.");
            if (macro.Mode is "WhileHeld" or "Toggle" && macro.Steps.All(s => s.DelayMs == 0)) throw new InvalidDataException("Repeating macros need a nonzero delay.");
        }
        foreach (var action in Buttons.Concat(ShiftButtons))
        {
            if (action.Kind is "Native" or "Default" or "Disabled" or "MouseRight" or "MouseMiddle" or "MouseBack" or "MouseForward" or "ScrollLeft" or "ScrollRight" or "ScrollUp" or "ScrollDown" or "DpiShift" or "DpiUp" or "DpiDown" or "DpiCycle" or "ProfileNext" or "GShift") continue;
            if (action.Kind == "Key" && ValidKeys(action.Keys)) continue;
            if (action.Kind == "Macro" && Macros.Any(m => m.Id == action.Target)) continue;
            if (action.Kind == "Launch" && action.Target != null && Path.IsPathFullyQualified(action.Target) && Path.GetExtension(action.Target).Equals(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (action.Kind == "Url" && Uri.TryCreate(action.Target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") continue;
            throw new InvalidDataException("An assignment is invalid or refers to a missing macro.");
        }
    }
    public static bool ValidKeys(int[]? keys) => keys?.Length is > 0 and <= 8 && keys.All(k => k is > 0 and <= 254) && keys.Distinct().Count() == keys.Length;
    public static void Save(string path, LogitechControlProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true })); File.Move(path + ".tmp", path, true);
    }
    public static LogitechControlProfile Load(string path)
    {
        if (new FileInfo(path).Length > 262144) throw new InvalidDataException("The control profile is too large.");
        return JsonSerializer.Deserialize<LogitechControlProfile>(File.ReadAllText(path)) ?? throw new InvalidDataException("The control profile is empty.");
    }
}

/// <summary>Physical bitmap transitions. A release always uses the action captured at press time.</summary>
public sealed class LogitechButtonState
{
    private LogitechControlProfile profile;
    private readonly Dictionary<int, LogitechAction> held = [];
    public int Stage { get; private set; }
    public bool ShiftHeld => held.Values.Any(a => a.Kind == "DpiShift");
    public bool GShiftHeld => held.Values.Any(a => a.Kind == "GShift");
    public int CurrentDpi => ShiftHeld ? profile.ShiftDpi : profile.Stages[Stage];
    public ushort Bitmap { get; private set; }
    public LogitechButtonState(LogitechControlProfile profile) { this.profile = profile; Stage = profile.DefaultStage; }
    public void UseProfile(LogitechControlProfile next, int? stage = null)
    {
        profile = next;
        Stage = stage is >= 0 && stage < next.Stages.Length ? stage.Value : next.DefaultStage;
    }
    public IReadOnlyList<(int Button, LogitechAction Action, bool Down)> Update(ushort bitmap)
    {
        bitmap &= 0x7ff;
        var result = new List<(int, LogitechAction, bool)>();
        for (var i = 1; i < 11; i++) if ((Bitmap & 1 << i) != 0 && (bitmap & 1 << i) == 0 && held.Remove(i, out var action)) result.Add((i, action, false));
        // Resolve modifier buttons before other buttons arriving in the same report.
        var pressed = Enumerable.Range(1, 10).Where(i => (Bitmap & 1 << i) == 0 && (bitmap & 1 << i) != 0).OrderBy(i => profile.Buttons[i].Kind == "GShift" ? 0 : 1).ToArray();
        foreach (var i in pressed)
        {
            var action = LogitechButtonRouting.RuntimeAction(profile, i, GShiftHeld);
            held[i] = action;
            switch (action.Kind)
            {
                case "DpiUp":
                {
                    var current = profile.Stages[Stage];
                    var higher = profile.Stages.Select((dpi, index) => (dpi, index)).Where(x => x.dpi > current).OrderBy(x => x.dpi).FirstOrDefault();
                    if (higher.dpi > current) Stage = higher.index;
                    break;
                }
                case "DpiDown":
                {
                    var current = profile.Stages[Stage];
                    var lower = profile.Stages.Select((dpi, index) => (dpi, index)).Where(x => x.dpi < current).OrderByDescending(x => x.dpi).FirstOrDefault();
                    if (lower.dpi > 0 && lower.dpi < current) Stage = lower.index;
                    break;
                }
                case "DpiCycle": Stage = (Stage + 1) % profile.Stages.Length; break;
            }
            result.Add((i, action, true));
        }
        Bitmap = bitmap; return result;
    }
}

using Sora.Core;

static class LogitechRuntimeChecks
{
    public static async Task Run()
    {
        void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        var profile = LogitechControlProfile.Create(); var state = new LogitechButtonState(profile);
        Require(profile.Buttons[8].Kind == "ProfileNext", "G9 defaults to profile cycling in Sora software control");
        Require(new LogitechButtonState(profile).Update(0x100).Any(t => t.Button == 8 && t.Action.Kind == "ProfileNext" && t.Down), "G9 notification dispatches the profile-cycle action");
        Require(state.CurrentDpi == 800, "Default DPI");
        state.Update(0x20); Require(state.CurrentDpi == 4000 && state.ShiftHeld, "G6 press changes to configured shift speed");
        state.Update(0); Require(state.CurrentDpi == 800 && !state.ShiftHeld, "G6 release returns to previous normal stage");
        state.Update(0x80); Require(state.CurrentDpi == 1600, "G8 goes up one stage");
        state.Update(0x80); Require(state.CurrentDpi == 1600, "Repeated held notification is not a second press");
        state.Update(0); state.Update(0x20); state.Update(0); Require(state.CurrentDpi == 1600, "Shift restores the changed normal stage rather than a hardcoded default");
        state.Update(0x40); Require(state.CurrentDpi == 800, "G7 goes down one stage");
        state.Update(0); state.Update(0x40); state.Update(0); state.Update(0x40); Require(state.CurrentDpi == 400, "DPI Down stops at first stage");

        var unsorted = new LogitechButtonState(profile with { Stages = [800, 400, 1600], DefaultStage = 0 });
        unsorted.Update(0x80); Require(unsorted.CurrentDpi == 1600, "DPI Up chooses the next numeric speed when configured stages are unsorted");
        unsorted.Update(0); unsorted.Update(0x40); Require(unsorted.CurrentDpi == 800, "DPI Down chooses the next numeric speed when configured stages are unsorted");
        unsorted.Update(0); unsorted.Update(0x40); Require(unsorted.CurrentDpi == 400, "DPI Down reaches an unsorted minimum");
        unsorted.Update(0); unsorted.Update(0x40); Require(unsorted.CurrentDpi == 400, "DPI Down at an unsorted minimum cannot jump up to the first array entry");
        Require(LogitechDpiIndicator.Level(profile, 800) == 2 && LogitechDpiIndicator.Level(profile, profile.ShiftDpi) == 4, "Indicator slots follow numeric speeds including the separate Shift speed");

        var alternate = profile.ShiftButtons.ToArray(); alternate[1] = new("Key", "Copy", [17, 67]);
        var buttons = profile.Buttons.ToArray(); buttons[5] = new("GShift", "G-Shift");
        var gshift = new LogitechButtonState(profile with { Buttons = buttons, ShiftButtons = alternate });
        var press = gshift.Update(0x22); Require(press.Any(t => t.Button == 1 && t.Action.Kind == "Key" && t.Down), "Simultaneous G-Shift+G2 resolves the alternate layer");
        gshift.Update(0x2); var release = gshift.Update(0); Require(release.Single().Action.Kind == "Key", "Button release uses the action captured before the G-Shift modifier was released");

        var fake = new Output();
        await using (var runner = new LogitechActionRunner(fake, profile))
        {
            var chord = new LogitechAction("Key", "Copy", [17, 67]);
            await runner.HandleAsync(1, chord, true); await runner.HandleAsync(1, profile.Buttons[1], false);
            Require(fake.Events.SequenceEqual(new[] { "17 down", "67 down", "67 up", "17 up" }), "Shortcut presses/releases follow captured action and reverse modifier order");
        }
        var repeating = new LogitechMacro("repeat", "Repeat A", "WhileHeld", [new("Press", 1, [65])]);
        fake = new Output();
        await using (var runner = new LogitechActionRunner(fake, profile with { Macros = [repeating] }))
        {
            var action = new LogitechAction("Macro", "Repeat A", Target: "repeat");
            await runner.HandleAsync(5, action, true); await fake.FirstDown.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await runner.HandleAsync(5, action, false); Require(fake.Held == 0, "Repeat-while-held cancellation releases the active key");
        }
        var sequence = new LogitechMacro("sequence", "Sequence", "Sequence", [new("Press", 1, [65]), new("Hold", 1, [66]), new("Release", 10000, [67])]);
        fake = new Output();
        var sequenceRunner = new LogitechActionRunner(fake, profile with { Macros = [sequence] });
        await sequenceRunner.HandleAsync(5, new("Macro", "Sequence", Target: "sequence"), true); await fake.FirstDown.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await sequenceRunner.HandleAsync(5, new("Macro", "Sequence", Target: "sequence"), false).WaitAsync(TimeSpan.FromSeconds(1));
        await sequenceRunner.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1)); Require(fake.Held == 0, "Stopping a delayed release sequence never blocks shutdown or leaves input held");
    }
    private sealed class Output : ILogitechOutput
    {
        public readonly List<string> Events = [];
        public readonly TaskCompletionSource FirstDown = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Held;
        public void Key(int key, bool down) { Events.Add($"{key} {(down ? "down" : "up")}"); Held += down ? 1 : -1; if (down) FirstDown.TrySetResult(); }
        public void Mouse(string kind, bool down) { Held += down ? 1 : -1; }
        public void Scroll(bool horizontal, int delta) { }
        public void Launch(string target, bool url) { }
        public void ReleaseAll() { Held = 0; }
    }
}

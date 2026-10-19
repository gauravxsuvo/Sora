using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Sora.Core;

namespace Sora.App;

public partial class LightingPanel
{
    /// <summary>Deterministic WPF editor scenarios; synthetic state only, no Scan, Apply, SMBus or HID operations.</summary>
    internal static bool RunEditorChecks(string output)
    {
        var checks = new List<object>();
        var failure = false;
        var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "lighting-editor-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        LightingPanel? panel = null;
        try
        {
            void Check(bool condition, string name)
            {
                checks.Add(new { name, passed = condition });
                if (!condition) failure = true;
            }
            foreach (var effect in Enum.GetValues<SyncEffect>())
            {
                var animatedSettings = new LightingSettings(Sync: true, SyncLighting: new SyncLighting(effect));
                var valid = true;
                foreach (var allowed in new[] { false, true })
                foreach (var suspended in new[] { false, true })
                foreach (var running in new[] { false, true })
                foreach (var synced in new[] { false, true })
                {
                    var actual = LightingService.ShouldStartSyncEngine(allowed, suspended, animatedSettings with { Sync = synced }, running);
                    var expected = allowed && !suspended && !running && synced && effect is SyncEffect.ColorCycle or SyncEffect.Breathing or SyncEffect.Flashing;
                    valid &= actual == expected;
                }
                Check(valid, $"Sync lifecycle permission/suspend/ownership guard for {effect} (16 combinations)");
            }
            Check(!LightingService.ShouldStartSyncEngine(true, false, new LightingSettings(Sync: true), false),
                "A missing sync effect cannot start a worker");
            const string model = "Z890 GAMING X WIFI7";
            var catalog = GigabyteRgb.ZonesFor(model);
            var red = new MemoryLighting(EneMode.Static, "#FF0000", 90, 2);
            var blue = new MemoryLighting(EneMode.Static, "#0000FF", 70, 4);
            var modules = new[] { new ModuleLighting(1, "Fixture ENE", 0x6802, red), new ModuleLighting(3, "Fixture ENE", 0x6802, blue) };
            var board = catalog.Select(z => new BoardZoneLighting(z.Number, BoardEffect.Static, "#20A050", 5, 4)).ToArray();
            var settings = new LightingSettings(BoardZones: board, ApplyAtStartup: false, RainbowOnExit: false, MemorySync: false, MemoryModules: modules);
            File.WriteAllText(Path.Combine(directory, "lighting.json"), JsonSerializer.Serialize(settings));
            var host = new SoraHost(directory);
            var lighting = host.Lighting;
            lighting.SetSuspended(true);
            EneDimm Dimm(int slot, byte r, byte b, byte g) => new(slot, (byte)(0x70 + slot), "Fixture ENE", 0x6802, [0xA1, 0xD5, 8, 3, 0], 8,
                0x8020, 0x8160, new EneSnapshot([0, (byte)EneMode.Static, 0, 0], Enumerable.Range(0, 8).SelectMany(_ => new[] { r, b, g }).ToArray()));
            var state = new LightingState([Dimm(1, 255, 0, 0), Dimm(3, 0, 255, 0)], "Synthetic memory", null,
                new BoardInfo("Fixture IT5711", 0, 0, 0, 0, 0, 0, ""), null, null, false, DateTimeOffset.UtcNow, model, catalog);
            // Diagnostic-only injection avoids adding a production hardware abstraction or making State mutable.
            typeof(LightingService).GetProperty(nameof(LightingService.State), BindingFlags.Instance | BindingFlags.Public)!.SetValue(lighting, state);
            panel = new LightingPanel();
            panel.Attach(lighting);
            Check(panel.moduleLooks.Count == 2 && panel.moduleLooks[1].Lighting == red && panel.moduleLooks[3].Lighting == blue,
                "Independent module settings populate both physical-slot identities");
            Check(!panel.HasPendingEdits && !panel.MemorySyncToggle.IsChecked.GetValueOrDefault(), "Loading independent settings stages no hardware changes");

            void SelectModule(int slot) => panel.ModulePicker.SelectedItem = panel.ModulePicker.Items.Cast<ModuleChoice>().Single(m => m.Slot == slot);
            SelectModule(1);
            panel.MemoryHex.Text = "#00FF00";
            Check(panel.moduleLooks[1].Lighting.Color == "#00FF00" && panel.moduleLooks[3].Lighting == blue && panel.dirtyModules.SetEquals([1]),
                "Editing A2 preserves B2 and dirties only A2");
            SelectModule(3);
            Check(panel.MemoryHex.Text == blue.Color && panel.MemoryBrightness.Value == blue.Brightness && panel.MemorySpeed.Value == blue.Speed,
                "Switching the module editor shows that module's independent controls");
            panel.MemoryHex.Text = "#FFFF00";
            Check(panel.moduleLooks[1].Lighting.Color == "#00FF00" && panel.moduleLooks[3].Lighting.Color == "#FFFF00" && panel.dirtyModules.SetEquals([1, 3]),
                "Edits survive switching modules and accumulate explicit targets");
            panel.MemorySyncToggle.IsChecked = true;
            Check(panel.moduleLooks.Values.All(m => m.Lighting == panel.EditedMemory) && panel.dirtyModules.SetEquals([1, 3]) && lighting.Settings.MemorySync,
                "Enabling RAM sync stages the selected look for both modules");
            Check(lighting.Settings.MemoryModules!.SequenceEqual(modules), "Staged RAM sync edits do not overwrite applied module settings");
            panel.MemorySyncToggle.IsChecked = false;
            Check(!lighting.Settings.MemorySync && panel.moduleLooks.Values.All(m => m.Lighting.Color == "#FFFF00"), "Disabling RAM sync keeps staged per-module looks");

            var ioChip = panel.BoardZonesPanel.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>().Single(c => (int)c.Tag == 9);
            ioChip.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(panel.selectedZones.SetEquals([9]), "A plain I/O-cover click selects that light alone from the initial all-zone selection");
            panel.BoardHex.Text = "#BF5AF2";
            Check(panel.dirtyZones.SetEquals([9]) && panel.boardZones.Single(z => z.Zone == 9).Color == "#BF5AF2"
                && panel.boardZones.Where(z => z.Zone != 9).SequenceEqual(board.Where(z => z.Zone != 9)),
                "I/O-cover color edits preserve every unselected motherboard zone");
            panel.BoardEffectPicker.SelectedItem = panel.BoardEffectPicker.Items.Cast<ComboBoxItem>().Single(i => ((BoardOption)i.Tag).Effect == BoardEffect.Rainbow);
            Check(panel.dirtyZones.SetEquals([9]) && panel.boardZones.Count(z => z.Effect == BoardEffect.Rainbow) == 1,
                "I/O-cover Rainbow stays a one-zone staged request");
            panel.selectedZones.Clear(); panel.selectedZones.Add(2); panel.LoadZoneEditor();
            Check(panel.dirtyZones.SetEquals([9]), "Changing selected zones does not retarget prior pending edits");
            panel.LoadMemoryControls();
            Check(panel.boardDirty && panel.dirtyZones.SetEquals([9]) && panel.boardZones.Single(z => z.Zone == 9).Effect == BoardEffect.Rainbow,
                "Refreshing memory controls preserves pending motherboard edits");

            panel.SyncToggle.IsChecked = true;
            Check(lighting.Settings.Sync && lighting.Settings.SyncLighting == null && panel.syncDirty,
                "Global sync preference remains unapplied until Apply");
            Check(lighting.Settings.MemoryModules!.SequenceEqual(modules) && lighting.Settings.BoardZones!.SequenceEqual(board),
                "Toggling global sync does not rewrite applied hardware sections");
            panel.SyncToggle.IsChecked = false;
            Check(!lighting.Settings.Sync && !panel.HasPendingEdits && panel.moduleLooks[1].Lighting == red && panel.moduleLooks[3].Lighting == blue,
                "Leaving unapplied global sync restores independently applied module looks");

            SelectModule(1); panel.MemoryHex.Text = "invalid";
            Check(ControlState.GetIsInvalid(panel.MemoryHex) && panel.MemoryColorError.Text == ControlState.HexColourError
                && System.Windows.Automation.AutomationProperties.GetHelpText(panel.MemoryHex) == ControlState.HexColourError,
                "Memory colour uses shared invalid outline, explanation and accessible help");
            panel.MemoryHex.Text = "#123ABC";
            Check(!ControlState.GetIsInvalid(panel.MemoryHex) && panel.MemoryColorError.Text.Length == 0,
                "Correcting a memory hex colour clears shared validation");
            panel.MemoryHex.Text = "invalid";
            SelectModule(3);
            Check(!panel.ApplyButton.IsEnabled, "An invalid pending module color remains blocked after selecting another module");
            panel.DiscardClicked(panel, new RoutedEventArgs());
            Check(!panel.HasPendingEdits && panel.moduleLooks[1].Lighting == red && panel.moduleLooks[3].Lighting == blue,
                "Discard restores applied looks and clears dirty targets");
            ioChip = panel.BoardZonesPanel.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>().Single(c => (int)c.Tag == 9);
            ioChip.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            panel.BoardHex.Text = "invalid";
            var ssdChip = panel.BoardZonesPanel.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>().Single(c => (int)c.Tag == 2);
            ssdChip.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(!panel.ApplyButton.IsEnabled && panel.invalidZoneColors.ContainsKey(9), "An invalid pending board color remains blocked after selecting a different zone");
            ioChip.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(panel.BoardHex.Text == "invalid", "Returning to a zone restores its invalid draft for correction");
            panel.DiscardClicked(panel, new RoutedEventArgs());
            var reloaded = new LightingService(host); reloaded.SetSuspended(true); reloaded.Load();
            Check(!reloaded.Settings.MemorySync && !reloaded.Settings.Sync && reloaded.Settings.MemoryModules!.SequenceEqual(modules)
                && reloaded.Settings.BoardZones!.SequenceEqual(board), "Preference JSON reload retains per-module and per-zone applied settings");

            var greenModule = modules[0] with { Lighting = red with { Color = "#00FF00" } };
            var oneZone = board.Single(z => z.Zone == 9) with { Color = "#BF5AF2" };
            var merged = LightingService.MergeAppliedSettings(settings, null, [greenModule], [oneZone], null, true);
            Check(merged.MemoryModules!.Count == 2 && merged.MemoryModules.Single(m => m.Slot == 1) == greenModule
                && merged.MemoryModules.Single(m => m.Slot == 3) == modules[1], "Confirmed partial module updates preserve the other module's saved look");
            Check(merged.BoardZones!.Count == board.Length && merged.BoardZones.Single(z => z.Zone == 9) == oneZone
                && merged.BoardZones.Where(z => z.Zone != 9).OrderBy(z => z.Zone).SequenceEqual(board.Where(z => z.Zone != 9).OrderBy(z => z.Zone)), "Confirmed partial board updates preserve every other saved zone");
            var legacySettings = settings with { Memory = blue, MemoryModules = null };
            var legacyModules = modules.Select(m => m with { Lighting = blue }).ToArray();
            var migrated = LightingService.MergeAppliedSettings(legacySettings, null, [greenModule], null, legacyModules, false);
            Check(migrated.MemoryModules!.Count == 2 && migrated.MemoryModules.Single(m => m.Slot == 3).Lighting == blue
                && migrated.MemoryModules.Single(m => m.Slot == 1) == greenModule, "The first partial edit materializes unedited legacy RAM looks for sign-in restoration");
            var noChanges = LightingService.MergeAppliedSettings(settings, null, null, null, legacyModules, true);
            Check(noChanges.MemoryModules!.SequenceEqual(modules) && noChanges.BoardZones!.SequenceEqual(board)
                && noChanges.ApplyAtStartup && noChanges.Memory == settings.Memory, "Failed or skipped sections never replace saved module or board looks");
            var unified = LightingService.MergeAppliedSettings(merged, blue, legacyModules, null, null, false);
            Check(unified.Memory == blue && unified.MemoryModules!.SequenceEqual(legacyModules)
                && unified.BoardZones!.SequenceEqual(merged.BoardZones!), "Confirmed unified RAM updates replace module looks while preserving the board");

            // Reproduce saved sync Off coexisting with preview-era independent red/blue target looks.
            foreach (var effect in Enum.GetValues<SyncEffect>())
            {
                var sync = new SyncLighting(effect, "#12ABEF", 80, 4);
                var stale = settings with { Sync = true, SyncLighting = sync, Memory = red };
                var startup = LightingService.ResolveStartupLook(stale, catalog);
                Check(startup.Memory == sync.ToMemory() && startup.MemoryModules == null
                    && startup.BoardZones!.SequenceEqual(sync.ToBoard(catalog.Select(z => z.Number)))
                    && stale.MemoryModules!.SequenceEqual(modules) && stale.BoardZones!.SequenceEqual(board),
                    $"Startup {effect} resolves the common saved effect over stale individual looks without mutating its input");
            }
            Check(LightingService.ResolveStartupLook(settings, catalog) == settings,
                "Independent startup retains per-module and per-zone looks");
            var unappliedSync = settings with { Sync = true, SyncLighting = null };
            Check(LightingService.ResolveStartupLook(unappliedSync, catalog) == unappliedSync,
                "A sync toggle without a chosen saved effect does not invent a startup look");

            var savedOff = settings with { Sync = true, SyncLighting = new SyncLighting(SyncEffect.Off) };
            lighting.RestoreEdits(savedOff); panel.LoadControls(); panel.Render();
            Check(!lighting.SyncApplied && panel.StatusText.Text == "Saved sync · Lights off"
                && panel.ChangeSummary.Text == "Saved sync not applied" && panel.StatusTone == "Warn" && panel.ApplyButton.IsEnabled,
                "Saved Off without a successful apply stays pending and can be explicitly reapplied");
            typeof(LightingService).GetField("appliedSyncLighting", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(lighting, savedOff.SyncLighting);
            panel.Render();
            Check(lighting.SyncApplied && panel.StatusText.Text == "Synced · Lights off"
                && panel.ChangeSummary.Text == "In sync" && !panel.ApplyButton.IsEnabled,
                "Only a successful matching complete application permits the synced status");
            lighting.RememberEdits(null, null, null, savedOff.SyncLighting! with { Effect = SyncEffect.Static });
            panel.LoadControls(); panel.Render();
            Check(!lighting.SyncApplied && panel.StatusText.Text.StartsWith("Saved sync · ") && panel.ApplyButton.IsEnabled,
                "A newer persisted sync edit invalidates an older successful application");
            typeof(LightingService).GetField("appliedSyncLighting", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(lighting, lighting.Settings.SyncLighting);
            typeof(LightingService).GetProperty(nameof(LightingService.State), BindingFlags.Instance | BindingFlags.Public)!.SetValue(lighting, state with { BoardError = "fixture read failure" });
            panel.Render();
            Check(!lighting.SyncApplied && panel.StatusTone == "Warn", "A failed hardware scan cannot present a healthy synced state");
            typeof(LightingService).GetProperty(nameof(LightingService.State), BindingFlags.Instance | BindingFlags.Public)!.SetValue(lighting, state);
            lighting.SetSuspended(true); panel.Render();
            Check(!lighting.SyncApplied, "Suspend invalidates the old successful sync application before resume");
            typeof(LightingService).GetProperty(nameof(LightingService.State), BindingFlags.Instance | BindingFlags.Public)!.SetValue(lighting,
                state with { Dimms = [], Board = null, MemoryError = "fixture unavailable", BoardError = "fixture unavailable" });
            panel.Render();
            Check(panel.AvailabilityNotice.Visibility == Visibility.Visible && panel.AvailabilityNotice.Content.ToString()!.Contains("Offline"), "Unavailable controllers have explicit offline guidance");
            Check(panel.MemorySyncToggle.IsEnabled && panel.SyncToggle.IsEnabled && !panel.ApplyButton.IsEnabled && !panel.RestoreButton.IsEnabled && !panel.KeepOnBoard.IsEnabled,
                "Offline preferences stay editable while hardware operations are disabled");
            Check(panel.ApplyDetail.Text.Contains("No lighting has been sent"), "Offline footer never claims automatic hardware application");
            lighting.AutomaticChangesAllowed = true;
            var offlineBaseline = lighting.Settings;
            panel.SyncHex.Text = "#123ABC";
            Check(lighting.Settings.SyncLighting?.Color == "#123ABC" && panel.HasPendingEdits && !panel.ApplyButton.IsEnabled && !panel.autoApplyTimer.IsEnabled,
                "Offline edits save local intent without starting a hardware apply timer");
            Check(panel.ApplyDetail.Text.Contains("not sent to hardware"), "Locally saved edits explicitly remain unsent");
            panel.ApplyClicked(panel, new RoutedEventArgs());
            Check(!lighting.Busy && panel.ApplyDetail.Text.Contains("Lighting was not sent"), "Unavailable Apply is rejected before hardware operations");
            panel.DiscardClicked(panel, new RoutedEventArgs());
            Check(lighting.Settings == offlineBaseline && !panel.HasPendingEdits, "Discard restores the previous saved offline look");
            lighting.AutomaticChangesAllowed = false;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            failure = true;
            checks.Add(new { name = "Fixture or scenario exception", passed = false, error = error.ToString() });
        }
        finally { panel?.conflictTimer.Stop(); }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { passed = !failure, lightingWrites = 0, hardwareReads = 0, fixtureDirectory = directory, checks }, new JsonSerializerOptions { WriteIndented = true }));
        return !failure;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Threading;
using System.Threading;
using Sora.Core;

namespace Sora.App;

internal static class LegionUiCheck
{
    internal static async Task<bool> RunAsync(string output)
    {
        output = Path.GetFullPath(output);
        if (File.Exists(output) || File.Exists(output + ".overview.png") || File.Exists(output + ".lighting.png")) throw new IOException("Choose a new UI evidence path.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var checks = new Dictionary<string, bool>();
        var transport = new FixtureTransport();
        var interactive = new FixtureTransport(allowWrites: true);
        var controller = new LenovoLegionController(transport, new FixtureRecovery());
        var page = new LegionPanel { Background = (Brush)Application.Current.FindResource("WindowBrush") };
        page.Attach(controller, () => true);
        var window = new Window { Title = "Sora Legion UI check", Content = page, Width = 1060, Height = 820,
            Left = -12000, Top = 0, ShowInTaskbar = false, Background = (Brush)Application.Current.FindResource("WindowBrush"),
            Foreground = (Brush)Application.Current.FindResource("TextBrush"), FontFamily = (FontFamily)Application.Current.FindResource("UIFont") };
        window.Show();
        try
        {
            var snapshot = await controller.ReadAsync();
            page.ShowState(snapshot);
            checks["Power and charging controls follow readable capabilities"] = page.ApplyPower.IsEnabled && page.ApplyCharge.IsEnabled && page.PowerPicker.Items.Count == 3;
            checks["Four-zone and Spectrum controls have distinct selectors"] = page.ApplyLighting.IsEnabled && page.ApplySpectrum.IsEnabled && page.KeyboardPicker.Items.Count == 1 && page.SpectrumPicker.Items.Count == 1;
            checks["Firmware GPU mode is explicit about restart"] = page.ApplyGpu.IsEnabled && page.CurrentGpu.Text.Contains("Firmware configuration");
            page.ShowState(snapshot with { ChargeMode = null, Keyboards = [], Spectrum = [], Gpu = null });
            checks["Missing interfaces do not enable their controls"] = page.ApplyPower.IsEnabled && !page.ApplyCharge.IsEnabled && !page.ApplyLighting.IsEnabled && !page.ApplySpectrum.IsEnabled && !page.ApplyGpu.IsEnabled;
            page.ShowState(snapshot with { Conflicts = ["LenovoLegionToolkit"] });
            checks["Competing controller disables every write action"] = new[] { page.ApplyPower, page.ApplyCharge, page.ApplyLighting, page.ApplySpectrum, page.ApplyGpu }.All(button => !button.IsEnabled);
            page.Attach(controller, () => false); page.ShowState(snapshot);
            checks["Read-only sessions disable every write action"] = new[] { page.ApplyPower, page.ApplyCharge, page.ApplyLighting, page.ApplySpectrum, page.ApplyGpu }.All(button => !button.IsEnabled);
            page.Attach(controller, () => true); page.ShowState(snapshot);
            var actions = new LenovoLegionController(interactive, new FixtureRecovery(allowWrites: true));
            page.Attach(actions, () => true);
            await CheckInteractions(page, interactive, checks);
            var scroll = (ScrollViewer)page.Content;
            scroll.ScrollToHome();
            await Task.Delay(200);
            window.UpdateLayout();
            Save(page, output + ".overview.png");
            scroll.ScrollToEnd(); await Task.Delay(100); window.UpdateLayout();
            Save(page, output + ".lighting.png");
            checks["No fixture configuration was changed"] = transport.Writes == 0;
            await controller.StopAsync();
            await actions.StopAsync();
            var passed = checks.Values.All(value => value);
            using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            JsonSerializer.Serialize(stream, new { passed, synthetic = true, configurationWrites = 0, simulatedConfigurationWrites = interactive.Writes, checks }, new JsonSerializerOptions { WriteIndented = true });
            return passed;
        }
        finally { window.Close(); }
    }
    private static void Save(FrameworkElement element, string path)
    {
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write); png.Save(file);
    }
    private static async Task Click(LegionPanel page, Button button)
    {
        if (!button.IsEnabled) throw new InvalidOperationException("Expected enabled button: " + button.Name + "; " + button.ToolTip);
        var invoke = (IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke);
        invoke.Invoke();
        await page.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        await page.PendingOperation.WaitAsync(TimeSpan.FromSeconds(15));
    }
    private static async Task CheckInteractions(LegionPanel page, FixtureTransport fixture, Dictionary<string, bool> checks)
    {
        void Check(string name, bool passed)
        {
            checks[name] = passed;
            if (!passed) throw new InvalidOperationException("Legion interaction check failed: " + name);
        }
        async Task Refresh() => await Click(page, page.RefreshButton);
        await Refresh();
        var original = await new LenovoLegionController(fixture, new FixtureRecovery(true)).ReadAsync();
        var items = page.PowerPicker.ItemsSource;
        page.ShowState(original);
        Check("Unchanged discovery does not rebuild dropdown collections", ReferenceEquals(items, page.PowerPicker.ItemsSource));
        page.ShowState(original with { PowerMode = LegionPowerMode.Quiet, ChargeMode = LegionChargeMode.Normal,
            Gpu = new(LegionGpuMode.Discrete, Enum.GetValues<LegionGpuMode>()),
            Spectrum = [new(original.Spectrum[0].Device, new(7, 2))] });
        Check("Untouched selections follow external configuration changes", page.PowerPicker.SelectedIndex == 0 && page.ChargePicker.SelectedIndex == 0 && page.GpuPicker.SelectedIndex == 3 && (int?)page.SpectrumProfile.SelectedItem == 2 && (int?)page.SpectrumBrightness.SelectedItem == 7);
        page.ShowState(original, preserveEdits: false);
        page.PowerPicker.SelectedIndex = 0; page.ChargePicker.SelectedIndex = 0; page.GpuPicker.SelectedIndex = 3;
        page.SpectrumProfile.SelectedItem = 4;
        page.ShowState(original with { PowerMode = LegionPowerMode.Performance, Spectrum = [new(original.Spectrum[0].Device, new(8, 1))] });
        Check("Refresh preserves edits and updates untouched Spectrum fields", page.PowerPicker.SelectedIndex == 0 && page.ChargePicker.SelectedIndex == 0 && page.GpuPicker.SelectedIndex == 3 && (int?)page.SpectrumProfile.SelectedItem == 4 && (int?)page.SpectrumBrightness.SelectedItem == 8);
        page.ShowState(original, preserveEdits: false);
        page.ShowState(original with { PowerMode = LegionPowerMode.Custom });
        Check("Unselectable custom mode does not enable a silent Apply", page.PowerPicker.SelectedItem == null && !page.ApplyPower.IsEnabled && page.ApplyPower.ToolTip != null);
        page.ShowState(original, preserveEdits: false);
        page.PowerPicker.SelectedIndex = -1; Check("Power Apply requires a selection", !page.ApplyPower.IsEnabled);
        page.ChargePicker.SelectedIndex = -1; Check("Charge Apply requires a selection", !page.ApplyCharge.IsEnabled);
        page.GpuPicker.SelectedIndex = -1; Check("GPU Apply requires a selection", !page.ApplyGpu.IsEnabled);
        page.SpectrumProfile.SelectedIndex = -1; Check("Spectrum Apply requires a profile", !page.ApplySpectrum.IsEnabled);
        page.EffectPicker.SelectedIndex = -1; Check("Lighting Apply requires an effect", !page.ApplyLighting.IsEnabled && page.OffLighting.IsEnabled);
        page.EffectPicker.SelectedIndex = 0; page.ShowState(original, preserveEdits: false);

        page.ChargePicker.SelectedIndex = 0; // An edit in another section must survive a power action.
        page.PowerPicker.SelectedIndex = 0;
        await Click(page, page.ApplyPower);
        Check("Power Apply button executes and retains another section's edit", fixture.Power == 1 && page.RestorePower.IsEnabled && page.ChargePicker.SelectedIndex == 0 && page.PowerFeedback.Text.Contains("verified"));
        await Click(page, page.RestorePower);
        Check("Power Restore button synchronizes its selector", fixture.Power == 2 && page.PowerPicker.SelectedIndex == 1 && !page.RestorePower.IsEnabled);
        await Click(page, page.ApplyCharge);
        Check("Charging Apply button executes the selected mode", fixture.ChargeFlags == 0 && page.RestoreCharge.IsEnabled);
        await Click(page, page.RestoreCharge);
        Check("Charging Restore button synchronizes its selector", fixture.ChargeFlags == 0x20 && page.ChargePicker.SelectedIndex == 1 && !page.RestoreCharge.IsEnabled);
        page.GpuPicker.SelectedIndex = 3;
        await Click(page, page.ApplyGpu);
        Check("GPU Apply button executes and explains restart", fixture.Gpu == LegionGpuMode.Discrete && page.GpuFeedback.Text.Contains("restart") && page.RestoreGpu.IsEnabled);
        await Click(page, page.RestoreGpu);
        Check("GPU Restore button synchronizes its selector", fixture.Gpu == LegionGpuMode.Hybrid && page.GpuPicker.SelectedIndex == 0 && !page.RestoreGpu.IsEnabled);
        page.SpectrumProfile.SelectedItem = 4; page.SpectrumBrightness.SelectedItem = 9;
        await Click(page, page.ApplySpectrum);
        Check("Spectrum Apply button executes both edited fields", fixture.Spectrum == new LegionSpectrumSetting(9, 4) && page.RestoreSpectrum.IsEnabled);
        await Click(page, page.RestoreSpectrum);
        Check("Spectrum Restore button updates profile and brightness", fixture.Spectrum == new LegionSpectrumSetting(6, 1) && (int?)page.SpectrumProfile.SelectedItem == 1 && (int?)page.SpectrumBrightness.SelectedItem == 6 && !page.RestoreSpectrum.IsEnabled);

        var before = fixture.Writes;
        page.Zone1.Text = "invalid";
        await Click(page, page.ApplyLighting);
        Check("Invalid static colors produce local feedback with no write", fixture.Writes == before && page.LightingFeedback.Text.Contains("#RRGGBB") && ControlState.GetIsInvalid(page.Zone1));
        var feedback = page.LightingFeedback.Text;
        await Refresh();
        Check("Refresh retains validation feedback beside the button", page.LightingFeedback.Text == feedback && page.StatusText.Text == feedback);
        page.Zone1.Text = "#38BDF8";
        Check("Editing an invalid color clears the stale validation marker", !ControlState.GetIsInvalid(page.Zone1));
        await Click(page, page.ApplyLighting);
        Check("Static Apply sends the selected zone colors", fixture.Lighting is { Length: 33 } report && report[2] == 1 && report[5] == 0x38 && report[6] == 0xbd && report[7] == 0xf8);
        page.Zone1.Text = "invalid";
        foreach (var index in new[] { 2, 3, 4 })
        {
            page.EffectPicker.SelectedIndex = index;
            await Click(page, page.ApplyLighting);
            Check("Effect " + index + " ignores unused color input", !page.Zone1.IsEnabled && fixture.Lighting![2] == (index == 2 ? 6 : 4));
        }
        await Click(page, page.OffLighting);
        Check("Lighting Off executes independently of color text", fixture.Lighting![2] == 0);
        page.Zone1.Text = "#38BDF8"; page.EffectPicker.SelectedIndex = 0;

        page.PowerPicker.SelectedIndex = 0;
        fixture.FailNextPowerWrite = true;
        await Click(page, page.ApplyPower);
        Check("Failed Apply reports recovery and permits retry without losing the target", fixture.Power == 2 && page.PowerPicker.SelectedIndex == 0 && page.ApplyPower.IsEnabled && page.PowerFeedback.Text.Contains("previous state was restored") && page.PowerFeedback.Text.Contains("Simulated failure after mutation"));
        feedback = page.PowerFeedback.Text;
        await Refresh();
        Check("Polling does not erase a failed transaction result", page.PowerFeedback.Text == feedback && page.StatusText.Text == feedback);
        await Click(page, page.ApplyPower);
        Check("Retry after recovery executes the retained selection", fixture.Power == 1 && page.PowerFeedback.Text.Contains("verified"));
        await Click(page, page.RestorePower);

        using (var gate = new ManualResetEventSlim())
        {
            fixture.WriteGate = gate; fixture.WriteEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            page.PowerPicker.SelectedIndex = 0;
            ((IInvokeProvider)new ButtonAutomationPeer(page.ApplyPower).GetPattern(PatternInterface.Invoke)).Invoke();
            await fixture.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var pending = page.PendingOperation;
            Check("In-flight mutation disables buttons and editable fields", !page.ApplyPower.IsEnabled && !page.RefreshButton.IsEnabled && !page.ChargePicker.IsEnabled && !page.Zone1.IsEnabled && page.PowerFeedback.Text.StartsWith("Working"));
            gate.Set(); await pending.WaitAsync(TimeSpan.FromSeconds(15));
            fixture.WriteGate = null; fixture.WriteEntered = null;
            Check("Completion always releases busy state", page.RefreshButton.IsEnabled && page.PowerPicker.IsEnabled && page.RestorePower.IsEnabled);
        }
        await Click(page, page.RestorePower);
        page.PowerPicker.SelectedIndex = 0; fixture.FailDiscoveryAfterPowerWrite = true;
        await Click(page, page.ApplyPower);
        Check("Post-command discovery failure is not presented as complete success", page.PowerFeedback.Text.Contains("could not refresh") && !page.ApplyPower.IsEnabled && page.RefreshButton.IsEnabled);
        fixture.DiscoveryFailed = false; fixture.FailDiscoveryAfterPowerWrite = false;
        await Refresh(); await Click(page, page.RestorePower);
        Check("Refresh recovers page usability after discovery failure", page.ApplyPower.IsEnabled && fixture.Power == 2);

        fixture.ConflictingControllers = ["LenovoLegionToolkit"];
        await Refresh();
        Check("Conflicts disable actions with an explanation", !page.ApplyPower.IsEnabled && Convert.ToString(page.ApplyPower.ToolTip)?.Contains("LenovoLegionToolkit") == true && !page.PowerPicker.IsEnabled);
        fixture.ConflictingControllers = []; await Refresh();
        var live = await new LenovoLegionController(fixture, new FixtureRecovery(true)).ReadAsync();
        page.ShowState(live with { ChargeMode = null, Keyboards = [], Spectrum = [], Gpu = null });
        Check("Missing interfaces disable inputs as well as buttons", !page.ChargePicker.IsEnabled && !page.GpuPicker.IsEnabled && !page.Zone1.IsEnabled && !page.SpectrumProfile.IsEnabled && page.ApplyPower.IsEnabled);
        page.ShowState(live);
        Check("Reconnected interfaces initialize their selections", page.ApplyCharge.IsEnabled && page.ApplyGpu.IsEnabled && page.ApplySpectrum.IsEnabled);
    }
    private sealed class FixtureRecovery(bool allowWrites = false) : ILegionRecoveryStore
    {
        public void Save(LegionRecovery recovery) { if (!allowWrites) throw new InvalidOperationException("Read-only UI checks must not apply controls."); }
    }
    private sealed class FixtureTransport(bool allowWrites = false) : ILegionTransport, ILegionSpectrumTransport, ILegionGpuTransport
    {
        public int Writes { get; private set; }
        public uint Power = 2, ChargeFlags = 0x20;
        public LegionGpuMode Gpu = LegionGpuMode.Hybrid;
        public LegionSpectrumSetting Spectrum = new(6, 1);
        public byte[]? Lighting;
        public bool FailNextPowerWrite, FailDiscoveryAfterPowerWrite, DiscoveryFailed;
        public ManualResetEventSlim? WriteGate;
        public TaskCompletionSource? WriteEntered;
        public string[] ConflictingControllers = [];
        public LenovoSystemIdentity Identity => DiscoveryFailed ? throw new IOException("Simulated discovery failure") : new("LENOVO", "82JQ", "Legion 5 Pro · UI fixture", "", "GKCN00WW", HardwarePresence.Present);
        public bool IsAcConnected() => true;
        public string[] Conflicts() => ConflictingControllers;
        public LegionPowerMode[] ReadSupportedPowerModes() => [LegionPowerMode.Quiet, LegionPowerMode.Balanced, LegionPowerMode.Performance];
        public uint ReadPowerMode() => Power;
        private void Write()
        {
            Writes++;
            if (!allowWrites) throw new InvalidOperationException("Read-only fixture was mutated.");
            WriteEntered?.TrySetResult();
            if (WriteGate != null && !WriteGate.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Simulated write gate timed out.");
        }
        public void WritePowerMode(uint value)
        {
            Write(); Power = value;
            if (FailNextPowerWrite) { FailNextPowerWrite = false; throw new IOException("Simulated failure after mutation"); }
            if (FailDiscoveryAfterPowerWrite) DiscoveryFailed = true;
        }
        public uint ChargeMode(uint request)
        {
            if (request == 255) return ChargeFlags;
            Write();
            ChargeFlags = request switch { 5 => ChargeFlags & ~0x20u, 8 => ChargeFlags & ~4u, 3 => ChargeFlags | 0x20u, 7 => ChargeFlags | 4u, _ => throw new InvalidOperationException() };
            return 0;
        }
        public DeviceInfo[] FourZoneDevices() => [new("ui-four-zone", "", 0xff89, 0xcc, 0, 33, 0x048d, 0xc985)];
        public void WriteFourZone(DeviceInfo device, byte[] report) { Write(); Lighting = report.ToArray(); }
        public DeviceInfo[] SpectrumDevices() => [new("ui-spectrum", "", 0xff89, 7, 0, 960, 0x048d, 0xc987)];
        public LegionSpectrumSetting ReadSpectrum(DeviceInfo device) => Spectrum;
        public void WriteSpectrum(DeviceInfo device, LegionSpectrumSetting setting) { Write(); Spectrum = setting; }
        public LegionGpuState ReadGpu() => new(Gpu, Enum.GetValues<LegionGpuMode>());
        public void WriteGpu(LegionGpuMode mode) { Write(); Gpu = mode; }
    }
}

using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Automation;
using Sora.Core;

namespace Sora.App;

public partial class LegionPanel : UserControl
{
    private sealed record Choice<T>(T Value, string Label);
    private LenovoLegionController? controller;
    private Func<bool> changesAllowed = () => false;
    private LegionState? state;
    private bool busy, updatingView;
    private string? actionStatus;
    internal Task PendingOperation { get; private set; } = Task.CompletedTask;
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    public LegionPanel()
    {
        InitializeComponent();
        ChargePicker.ItemsSource = new[] { new Choice<LegionChargeMode>(LegionChargeMode.Normal, "Normal"), new(LegionChargeMode.Conservation, "Conservation"), new(LegionChargeMode.RapidCharge, "Rapid charge") };
        EffectPicker.ItemsSource = new[] { new Choice<LegionFourZoneEffect>(LegionFourZoneEffect.Static, "Static"), new(LegionFourZoneEffect.Breathing, "Breathing"), new(LegionFourZoneEffect.ColorCycle, "Color cycle"), new(LegionFourZoneEffect.WaveLeft, "Wave left"), new(LegionFourZoneEffect.WaveRight, "Wave right") };
        EffectPicker.SelectedIndex = 0;
        SpeedPicker.ItemsSource = new[] { 1, 2, 3, 4 }; SpeedPicker.SelectedIndex = 1;
        BrightnessPicker.ItemsSource = new[] { 1, 2 }; BrightnessPicker.SelectedIndex = 1;
        SpectrumProfile.ItemsSource = Enumerable.Range(0, 7).ToArray();
        SpectrumBrightness.ItemsSource = Enumerable.Range(0, 10).ToArray();
        foreach (var picker in Pickers().Where(picker => picker != SpectrumPicker))
            picker.SelectionChanged += (_, _) => { if (!updatingView) UpdateButtons(); };
        foreach (var color in Colors()) color.TextChanged += (_, _) =>
        {
            ControlState.SetIsInvalid(color, false);
            AutomationProperties.SetHelpText(color, "");
        };
        SpectrumPicker.SelectionChanged += (_, _) =>
        {
            if (updatingView) return;
            if (SpectrumPicker.SelectedItem is Choice<LegionSpectrumState> choice)
            { SpectrumProfile.SelectedItem = (int)choice.Value.Setting.Profile; SpectrumBrightness.SelectedItem = (int)choice.Value.Setting.Brightness; }
            UpdateButtons();
        };
        refreshTimer.Tick += async (_, _) =>
        {
            // Do not rebuild a dropdown while it is open or interrupt color entry.
            if (IsVisible && !busy && !Pickers().Any(picker => picker.IsDropDownOpen) && !Colors().Any(color => color.IsKeyboardFocusWithin)) await ReadAsync();
        };
        IsVisibleChanged += (_, _) => UpdatePolling();
        Loaded += (_, _) => UpdatePolling();
        Unloaded += (_, _) => refreshTimer.Stop();
        UpdateButtons();
    }
    private ComboBox[] Pickers() => [PowerPicker, ChargePicker, GpuPicker, KeyboardPicker, EffectPicker, SpeedPicker, BrightnessPicker, SpectrumPicker, SpectrumProfile, SpectrumBrightness];
    private TextBox[] Colors() => [Zone1, Zone2, Zone3, Zone4];
    private void UpdatePolling()
    {
        if (IsLoaded && IsVisible && !SoraData.IsDevelopmentPreview) refreshTimer.Start(); else refreshTimer.Stop();
    }
    internal void Attach(LenovoLegionController value, Func<bool> allowed) { controller = value; changesAllowed = allowed; UpdateButtons(); }
    public async Task ReadAsync()
    {
        if (busy || controller == null || SoraData.IsDevelopmentPreview) return;
        busy = true; UpdateButtons();
        try { ShowState(await controller.ReadAsync()); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { state = null; StatusText.Text = "Could not refresh Legion controls: " + error.Message; ProblemsText.Text = "Refresh to retry. Previous action results are retained beside their controls."; }
        finally { busy = false; UpdateButtons(); }
    }
    private static void Select<T>(ComboBox picker, Choice<T>[] choices, Func<T, bool> matches)
    {
        if (!picker.Items.Cast<Choice<T>>().SequenceEqual(choices)) picker.ItemsSource = choices;
        picker.SelectedItem = picker.Items.Cast<Choice<T>>().FirstOrDefault(choice => matches(choice.Value));
    }
    internal void ShowState(LegionState snapshot, bool preserveEdits = true, TextBlock? appliedFeature = null)
    {
        var previous = state;
        var power = (PowerPicker.SelectedItem as Choice<LegionPowerMode>)?.Value;
        var charge = (ChargePicker.SelectedItem as Choice<LegionChargeMode>)?.Value;
        var gpu = (GpuPicker.SelectedItem as Choice<LegionGpuMode>)?.Value;
        var keyboardPath = (KeyboardPicker.SelectedItem as Choice<DeviceInfo>)?.Value.Path;
        var spectrum = (SpectrumPicker.SelectedItem as Choice<LegionSpectrumState>)?.Value;
        var profileEdit = SpectrumProfile.SelectedItem as int?;
        var brightnessEdit = SpectrumBrightness.SelectedItem as int?;
        var powerTarget = preserveEdits && appliedFeature != PowerFeedback && power != null && power != previous?.PowerMode ? power : snapshot.PowerMode;
        var chargeTarget = preserveEdits && appliedFeature != ChargeFeedback && charge != null && charge != previous?.ChargeMode ? charge : snapshot.ChargeMode;
        var gpuTarget = preserveEdits && appliedFeature != GpuFeedback && gpu != null && gpu != previous?.Gpu?.ConfiguredMode ? gpu : snapshot.Gpu?.ConfiguredMode;
        state = snapshot;
        updatingView = true;
        try
        {
            ModelText.Text = string.IsNullOrWhiteSpace(snapshot.Identity.Version) ? snapshot.Identity.Product : snapshot.Identity.Version;
            IdentityText.Text = snapshot.Identity.Product + " · BIOS " + snapshot.Identity.BiosVersion;
            CurrentPower.Text = "Current mode: " + (snapshot.PowerMode?.ToString() ?? "unavailable");
            CurrentCharge.Text = "Current charging mode: " + (snapshot.ChargeMode?.ToString() ?? "unavailable");
            CurrentGpu.Text = "Firmware configuration: " + (snapshot.Gpu?.ConfiguredMode.ToString() ?? "unavailable");
            Select(PowerPicker, snapshot.PowerModes.Select(mode => new Choice<LegionPowerMode>(mode, mode.ToString())).ToArray(), value => value == powerTarget);
            ChargePicker.SelectedItem = snapshot.ChargeMode == null ? null : ChargePicker.Items.Cast<Choice<LegionChargeMode>>().FirstOrDefault(choice => choice.Value == chargeTarget);
            Select(GpuPicker, snapshot.Gpu?.AvailableModes.Select(mode => new Choice<LegionGpuMode>(mode, mode switch
            { LegionGpuMode.Hybrid => "Hybrid", LegionGpuMode.IntegratedOnly => "Integrated graphics only", LegionGpuMode.Auto => "Automatic", _ => "Discrete graphics" })).ToArray() ?? [], value => value == gpuTarget);
            Select(KeyboardPicker, snapshot.Keyboards.Select((device, index) => new Choice<DeviceInfo>(device, "Keyboard " + (index + 1) + " · " + device.ProductId.ToString("X4"))).ToArray(), value => string.Equals(value.Path, keyboardPath, StringComparison.OrdinalIgnoreCase));
            if (KeyboardPicker.SelectedItem == null && snapshot.Keyboards.Length == 1) KeyboardPicker.SelectedIndex = 0;
            KeyboardText.Text = snapshot.Keyboards.Length == 0 ? "No supported four-zone interface detected. Spectrum and Corsair keyboards use separate controllers." : "Four-zone controller detected. Current colors are not readable.";
            Select(SpectrumPicker, snapshot.Spectrum.Select((item, index) => new Choice<LegionSpectrumState>(item, "Spectrum " + (index + 1) + " · " + item.Device.ProductId.ToString("X4"))).ToArray(), value => string.Equals(value.Device.Path, spectrum?.Device.Path, StringComparison.OrdinalIgnoreCase));
            if (SpectrumPicker.SelectedItem == null && snapshot.Spectrum.Length == 1) SpectrumPicker.SelectedIndex = 0;
            var liveSpectrum = (SpectrumPicker.SelectedItem as Choice<LegionSpectrumState>)?.Value;
            var sameSpectrum = liveSpectrum != null && string.Equals(liveSpectrum.Device.Path, spectrum?.Device.Path, StringComparison.OrdinalIgnoreCase);
            SpectrumProfile.SelectedItem = preserveEdits && appliedFeature != SpectrumFeedback && sameSpectrum && profileEdit != null && profileEdit != spectrum?.Setting.Profile ? profileEdit : liveSpectrum == null ? null : (int)liveSpectrum.Setting.Profile;
            SpectrumBrightness.SelectedItem = preserveEdits && appliedFeature != SpectrumFeedback && sameSpectrum && brightnessEdit != null && brightnessEdit != spectrum?.Setting.Brightness ? brightnessEdit : liveSpectrum == null ? null : (int)liveSpectrum.Setting.Brightness;
            SpectrumText.Text = snapshot.Spectrum.Length == 0 ? "No compatible Spectrum controller detected." : string.Join(" · ", snapshot.Spectrum.Select(item => "Current profile " + item.Setting.Profile + ", brightness " + item.Setting.Brightness));
            StatusText.Text = BlockReason() ?? actionStatus ?? "Available controls are shown below. Changes apply only when you press Apply.";
            ProblemsText.Text = string.Join(Environment.NewLine, snapshot.Problems);
        }
        finally { updatingView = false; UpdateButtons(); }
    }
    private string? BlockReason()
    {
        if (controller == null) return "Legion controls are not connected.";
        if (!changesAllowed()) return "Device changes are disabled while Sora is paused, stopping, or in read-only mode.";
        if (state == null) return "Refresh to read this laptop's available controls.";
        if (!state.Identity.IsLegionLaptop) return "A supported Lenovo Legion laptop was not detected.";
        if (state.Conflicts.Length != 0) return "Exit competing controllers before applying: " + string.Join(", ", state.Conflicts);
        return null;
    }
    private static void Enable(Control control, bool enabled, string reason)
    {
        control.IsEnabled = enabled;
        control.ToolTip = enabled ? null : reason;
        ToolTipService.SetShowOnDisabled(control, true);
    }
    private void UpdateButtons()
    {
        RefreshButton.IsEnabled = !busy && controller != null;
        var block = busy ? "Wait for the current Legion operation to finish." : BlockReason();
        var enabled = block == null;
        var power = enabled && state?.PowerMode != null && state.PowerModes.Length > 0;
        var charge = enabled && state?.ChargeMode != null;
        var gpu = enabled && state?.Gpu != null;
        var lighting = enabled && KeyboardPicker.SelectedItem != null;
        var spectrum = enabled && SpectrumPicker.SelectedItem != null;
        var powerReason = block ?? (!power ? "The performance-mode interface could not be read. Refresh to retry." : "Select a supported performance mode.");
        var chargeReason = block ?? (!charge ? "The Lenovo charging interface could not be read. Check the installed Lenovo Energy Management driver, then refresh." : "Select a charging mode.");
        var gpuReason = block ?? (!gpu ? "The graphics-mode interface could not be read. Refresh to retry." : "Select a supported graphics mode.");
        Enable(PowerPicker, power, block ?? "This firmware's performance-mode interface could not be read.");
        Enable(ChargePicker, charge, block ?? "The Lenovo charging interface could not be read. Check the installed Lenovo Energy Management driver.");
        Enable(GpuPicker, gpu, block ?? "This firmware's graphics-mode interface could not be read.");
        Enable(KeyboardPicker, enabled && state?.Keyboards.Length > 0, block ?? "No supported four-zone keyboard interface was detected.");
        Enable(SpectrumPicker, enabled && state?.Spectrum.Length > 0, block ?? "No compatible Spectrum interface could be read.");
        Enable(ApplyPower, power && PowerPicker.SelectedItem != null, powerReason);
        Enable(ApplyCharge, charge && ChargePicker.SelectedItem != null, chargeReason);
        Enable(ApplyGpu, gpu && GpuPicker.SelectedItem != null, gpuReason);
        Enable(RestorePower, power && controller?.CanRestorePower == true, !power ? powerReason : "An original mode becomes available after changing power mode in this session.");
        Enable(RestoreCharge, charge && controller?.CanRestoreCharge == true, !charge ? chargeReason : "An original mode becomes available after changing charging mode in this session.");
        Enable(RestoreGpu, gpu && controller?.CanRestoreGpu == true, !gpu ? gpuReason : "An original mode becomes available after changing graphics mode in this session.");
        foreach (var picker in new[] { EffectPicker, SpeedPicker, BrightnessPicker }) Enable(picker, lighting, block ?? "Select an available four-zone keyboard.");
        var validLighting = lighting && EffectPicker.SelectedItem != null && SpeedPicker.SelectedItem is int && BrightnessPicker.SelectedItem is int;
        Enable(ApplyLighting, validLighting, block ?? "Select a four-zone keyboard, effect, speed and brightness.");
        Enable(OffLighting, lighting, block ?? "Select an available four-zone keyboard.");
        var usesColors = EffectPicker.SelectedItem is Choice<LegionFourZoneEffect> effect && effect.Value is LegionFourZoneEffect.Static or LegionFourZoneEffect.Breathing;
        foreach (var color in Colors())
        {
            Enable(color, lighting && usesColors, block ?? "Zone colors are used by Static and Breathing effects.");
            if (!usesColors) { ControlState.SetIsInvalid(color, false); AutomationProperties.SetHelpText(color, ""); }
        }
        Enable(SpectrumProfile, spectrum, block ?? "Select an available Spectrum controller.");
        Enable(SpectrumBrightness, spectrum, block ?? "Select an available Spectrum controller.");
        Enable(ApplySpectrum, spectrum && SpectrumProfile.SelectedItem is int && SpectrumBrightness.SelectedItem is int, block ?? "Select a Spectrum controller, profile and brightness.");
        Enable(RestoreSpectrum, spectrum && SpectrumPicker.SelectedItem is Choice<LegionSpectrumState> choice && controller?.CanRestoreSpectrum(choice.Value.Device) == true, block ?? "Original Spectrum settings become available after changing this controller in this session.");
    }
    private void Feedback(TextBlock target, string message)
    {
        target.Text = message;
        actionStatus = message;
        StatusText.Text = message;
    }
    private void StartAction(Func<Task> operation, string success, TextBlock feedback, Button button)
    {
        if (!busy) PendingOperation = ApplyAsync(operation, success, feedback, button);
    }
    private async Task ApplyAsync(Func<Task> operation, string success, TextBlock feedback, Button button)
    {
        if (busy) return;
        if (BlockReason() is { } reason) { Feedback(feedback, reason); UpdateButtons(); return; }
        busy = true; UpdateButtons(); ControlState.SetIsBusy(button, true);
        Feedback(feedback, "Working… Waiting for the controller and configuration readback.");
        var applied = false;
        string result;
        try
        {
            try { await operation(); applied = true; result = success; }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                result = error.Message;
                if (error is LegionChangeException change)
                {
                    if (change.InnerException != null) result += " Cause: " + change.InnerException.Message;
                    if (change.RecoveryError != null) result += " Recovery: " + change.RecoveryError.Message;
                }
            }
            try
            {
                var snapshot = await controller!.ReadAsync();
                ShowState(snapshot, appliedFeature: applied ? feedback : null);
                var featureReadable = feedback == PowerFeedback ? snapshot.PowerMode != null
                    : feedback == ChargeFeedback ? snapshot.ChargeMode != null
                    : feedback == GpuFeedback ? snapshot.Gpu != null
                    : feedback == SpectrumFeedback ? snapshot.Spectrum.Length != 0 : snapshot.Keyboards.Length != 0;
                if (applied && !featureReadable) result = "The command completed, but this control's state could not be refreshed. Refresh before making another change.";
                if (snapshot.Problems.Length != 0) result += " Some controls could not be refreshed; see the interface details below.";
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                state = null;
                result = (applied ? "The command completed, but the page could not refresh. Refresh before making another change. " : result + " Refresh also failed. ") + error.Message;
                ProblemsText.Text = error.Message;
            }
            Feedback(feedback, result);
        }
        finally { busy = false; ControlState.SetIsBusy(button, false); UpdateButtons(); }
    }
    private void RefreshClick(object sender, RoutedEventArgs e) => PendingOperation = ReadAsync();
    private void ApplyPowerClick(object sender, RoutedEventArgs e)
    {
        if (PowerPicker.SelectedItem is Choice<LegionPowerMode> choice) StartAction(() => controller!.SetPowerAsync(choice.Value), "Power mode applied and verified.", PowerFeedback, ApplyPower);
        else Feedback(PowerFeedback, "Select a supported performance mode first.");
    }
    private void ApplyChargeClick(object sender, RoutedEventArgs e)
    {
        if (ChargePicker.SelectedItem is Choice<LegionChargeMode> choice) StartAction(() => controller!.SetChargeAsync(choice.Value), "Charging mode applied and verified.", ChargeFeedback, ApplyCharge);
        else Feedback(ChargeFeedback, "Select a charging mode first.");
    }
    private void RestorePowerClick(object sender, RoutedEventArgs e) => StartAction(() => controller!.RestorePowerAsync(), "Original session power mode restored and verified.", PowerFeedback, RestorePower);
    private void RestoreChargeClick(object sender, RoutedEventArgs e) => StartAction(() => controller!.RestoreChargeAsync(), "Original session charging mode restored and verified.", ChargeFeedback, RestoreCharge);
    private void ApplyLightingClick(object sender, RoutedEventArgs e)
    {
        if (KeyboardPicker.SelectedItem is not Choice<DeviceInfo> keyboard || EffectPicker.SelectedItem is not Choice<LegionFourZoneEffect> effect || SpeedPicker.SelectedItem is not int speed || BrightnessPicker.SelectedItem is not int brightness)
        { Feedback(LightingFeedback, "Select a keyboard, effect, speed and brightness first."); return; }
        byte[] colors = new byte[12];
        if (effect.Value is LegionFourZoneEffect.Static or LegionFourZoneEffect.Breathing)
        {
            TextBox? invalid = null;
            for (var index = 0; index < Colors().Length; index++)
            {
                var input = Colors()[index];
                try { ParseColor(input.Text).CopyTo(colors, index * 3); }
                catch (FormatException)
                {
                    invalid ??= input;
                    ControlState.SetIsInvalid(input, true);
                    AutomationProperties.SetHelpText(input, "Use #RRGGBB, for example #38BDF8.");
                }
            }
            if (invalid != null) { Feedback(LightingFeedback, "Use #RRGGBB for each highlighted zone color, for example #38BDF8."); invalid.Focus(); return; }
        }
        var report = LenovoLegionProtocol.FourZoneReport(effect.Value, (byte)speed, (byte)brightness, colors);
        StartAction(() => controller!.SetLightingAsync(keyboard.Value, report), "Lighting command sent. This controller does not provide color readback.", LightingFeedback, ApplyLighting);
    }
    private void OffLightingClick(object sender, RoutedEventArgs e)
    {
        if (KeyboardPicker.SelectedItem is Choice<DeviceInfo> keyboard)
            StartAction(() => controller!.SetLightingAsync(keyboard.Value, LenovoLegionProtocol.FourZoneOffReport()), "Lighting-off command sent.", LightingFeedback, OffLighting);
        else Feedback(LightingFeedback, "Select an available four-zone keyboard first.");
    }
    private static byte[] ParseColor(string text)
    {
        text = text.Trim();
        if (text.StartsWith('#')) text = text[1..];
        if (text.Length != 6 || !uint.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)) throw new FormatException();
        return [(byte)(value >> 16), (byte)(value >> 8), (byte)value];
    }
    private void ApplySpectrumClick(object sender, RoutedEventArgs e)
    {
        if (SpectrumPicker.SelectedItem is Choice<LegionSpectrumState> choice && SpectrumProfile.SelectedItem is int profile && SpectrumBrightness.SelectedItem is int brightness)
            StartAction(() => controller!.SetSpectrumAsync(choice.Value.Device, new((byte)brightness, (byte)profile)), "Spectrum profile and brightness applied and verified.", SpectrumFeedback, ApplySpectrum);
        else Feedback(SpectrumFeedback, "Select a Spectrum controller, profile and brightness first.");
    }
    private void ApplyGpuClick(object sender, RoutedEventArgs e)
    {
        if (GpuPicker.SelectedItem is Choice<LegionGpuMode> choice)
            StartAction(() => controller!.SetGpuAsync(choice.Value), "GPU firmware setting applied and verified. Active routing may require a restart.", GpuFeedback, ApplyGpu);
        else Feedback(GpuFeedback, "Select a supported graphics mode first.");
    }
    private void RestoreGpuClick(object sender, RoutedEventArgs e) => StartAction(() => controller!.RestoreGpuAsync(), "Original GPU firmware setting restored. Active routing may require a restart.", GpuFeedback, RestoreGpu);
    private void RestoreSpectrumClick(object sender, RoutedEventArgs e)
    {
        if (SpectrumPicker.SelectedItem is Choice<LegionSpectrumState> choice)
            StartAction(() => controller!.RestoreSpectrumAsync(choice.Value.Device), "Original Spectrum profile and brightness restored and verified.", SpectrumFeedback, RestoreSpectrum);
        else Feedback(SpectrumFeedback, "Select an available Spectrum controller first.");
    }
}

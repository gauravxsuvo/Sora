using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Sora.Core;

namespace Sora.App;

public partial class LegionPanel : UserControl
{
    private sealed record Choice<T>(T Value, string Label);
    private LenovoLegionController? controller;
    private Func<bool> changesAllowed = () => false;
    private LegionState? state;
    private bool busy;
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
        KeyboardPicker.SelectionChanged += (_, _) => UpdateButtons();
        SpectrumPicker.SelectionChanged += (_, _) =>
        {
            if (SpectrumPicker.SelectedItem is Choice<LegionSpectrumState> choice)
            { SpectrumProfile.SelectedItem = (int)choice.Value.Setting.Profile; SpectrumBrightness.SelectedItem = (int)choice.Value.Setting.Brightness; }
            UpdateButtons();
        };
        refreshTimer.Tick += async (_, _) => { if (IsVisible && !busy) await ReadAsync(); };
        IsVisibleChanged += (_, _) => { if (IsVisible && !SoraData.IsDevelopmentPreview) refreshTimer.Start(); else refreshTimer.Stop(); };
        Unloaded += (_, _) => refreshTimer.Stop();
    }
    internal void Attach(LenovoLegionController value, Func<bool> allowed) { controller = value; changesAllowed = allowed; }
    public async Task ReadAsync()
    {
        if (busy || controller == null || SoraData.IsDevelopmentPreview) return;
        busy = true; UpdateButtons();
        try { ShowState(await controller.ReadAsync()); }
        catch (Exception error) when (error is not OutOfMemoryException) { state = null; StatusText.Text = error.Message; ProblemsText.Text = ""; }
        finally { busy = false; UpdateButtons(); }
    }
    internal void ShowState(LegionState snapshot)
    {
        state = snapshot;
        ModelText.Text = string.IsNullOrWhiteSpace(snapshot.Identity.Version) ? snapshot.Identity.Product : snapshot.Identity.Version;
        IdentityText.Text = snapshot.Identity.Product + " · BIOS " + snapshot.Identity.BiosVersion;
        CurrentPower.Text = "Current mode: " + (snapshot.PowerMode?.ToString() ?? "unavailable");
        CurrentCharge.Text = "Current charging mode: " + (snapshot.ChargeMode?.ToString() ?? "unavailable");
        var selectedPower = (PowerPicker.SelectedItem as Choice<LegionPowerMode>)?.Value ?? snapshot.PowerMode;
        PowerPicker.ItemsSource = snapshot.PowerModes.Select(mode => new Choice<LegionPowerMode>(mode, mode.ToString())).ToArray();
        PowerPicker.SelectedItem = PowerPicker.Items.Cast<Choice<LegionPowerMode>>().FirstOrDefault(choice => choice.Value == selectedPower);
        ChargePicker.SelectedItem ??= ChargePicker.Items.Cast<Choice<LegionChargeMode>>().FirstOrDefault(choice => choice.Value == snapshot.ChargeMode);
        CurrentGpu.Text = "Firmware configuration: " + (snapshot.Gpu?.ConfiguredMode.ToString() ?? "unavailable");
        var selectedGpu = (GpuPicker.SelectedItem as Choice<LegionGpuMode>)?.Value ?? snapshot.Gpu?.ConfiguredMode;
        GpuPicker.ItemsSource = snapshot.Gpu?.AvailableModes.Select(mode => new Choice<LegionGpuMode>(mode, mode switch
        { LegionGpuMode.Hybrid => "Hybrid", LegionGpuMode.IntegratedOnly => "Integrated graphics only", LegionGpuMode.Auto => "Automatic", _ => "Discrete graphics" })).ToArray() ?? [];
        GpuPicker.SelectedItem = GpuPicker.Items.Cast<Choice<LegionGpuMode>>().FirstOrDefault(choice => choice.Value == selectedGpu);
        var selectedPath = (KeyboardPicker.SelectedItem as Choice<DeviceInfo>)?.Value.Path;
        KeyboardPicker.ItemsSource = snapshot.Keyboards.Select((device, index) => new Choice<DeviceInfo>(device, "Keyboard " + (index + 1) + " · " + device.ProductId.ToString("X4"))).ToArray();
        KeyboardPicker.SelectedItem = KeyboardPicker.Items.Cast<Choice<DeviceInfo>>().FirstOrDefault(choice => choice.Value.Path == selectedPath);
        if (KeyboardPicker.SelectedItem == null && snapshot.Keyboards.Length == 1) KeyboardPicker.SelectedIndex = 0;
        KeyboardText.Text = snapshot.Keyboards.Length == 0 ? "No supported four-zone interface detected. Spectrum and Corsair keyboards use separate controllers." : "Four-zone controller detected. Current colors are not readable.";
        var selectedSpectrum = (SpectrumPicker.SelectedItem as Choice<LegionSpectrumState>)?.Value.Device.Path;
        var profileEdit = SpectrumProfile.SelectedItem; var brightnessEdit = SpectrumBrightness.SelectedItem;
        SpectrumPicker.ItemsSource = snapshot.Spectrum.Select((item, index) => new Choice<LegionSpectrumState>(item, "Spectrum " + (index + 1) + " · " + item.Device.ProductId.ToString("X4"))).ToArray();
        SpectrumPicker.SelectedItem = SpectrumPicker.Items.Cast<Choice<LegionSpectrumState>>().FirstOrDefault(choice => choice.Value.Device.Path == selectedSpectrum);
        if (SpectrumPicker.SelectedItem == null && snapshot.Spectrum.Length == 1) SpectrumPicker.SelectedIndex = 0;
        if (selectedSpectrum != null && SpectrumPicker.SelectedItem != null) { SpectrumProfile.SelectedItem = profileEdit; SpectrumBrightness.SelectedItem = brightnessEdit; }
        SpectrumText.Text = snapshot.Spectrum.Length == 0 ? "No compatible Spectrum controller detected." : string.Join(" · ", snapshot.Spectrum.Select(item => "Current profile " + item.Setting.Profile + ", brightness " + item.Setting.Brightness));
        StatusText.Text = snapshot.Conflicts.Length == 0 ? "Available controls are shown below. Changes apply only when you press Apply." : "Exit competing controllers before applying: " + string.Join(", ", snapshot.Conflicts);
        ProblemsText.Text = string.Join(Environment.NewLine, snapshot.Problems);
        UpdateButtons();
    }
    private void UpdateButtons()
    {
        RefreshButton.IsEnabled = !busy;
        var enabled = !busy && changesAllowed() && state is { Conflicts.Length: 0 };
        ApplyPower.IsEnabled = enabled && state?.PowerMode != null && state.PowerModes.Length > 0;
        RestorePower.IsEnabled = enabled && controller?.CanRestorePower == true;
        ApplyCharge.IsEnabled = enabled && state?.ChargeMode != null;
        ApplyGpu.IsEnabled = enabled && state?.Gpu != null;
        RestoreGpu.IsEnabled = enabled && controller?.CanRestoreGpu == true;
        RestoreCharge.IsEnabled = enabled && controller?.CanRestoreCharge == true;
        ApplyLighting.IsEnabled = OffLighting.IsEnabled = enabled && KeyboardPicker.SelectedItem != null;
        ApplySpectrum.IsEnabled = enabled && SpectrumPicker.SelectedItem != null;
        RestoreSpectrum.IsEnabled = enabled && SpectrumPicker.SelectedItem is Choice<LegionSpectrumState> choice && controller?.CanRestoreSpectrum(choice.Value.Device) == true;
    }
    private async Task ApplyAsync(Func<Task> operation, string success)
    {
        if (busy || !changesAllowed() || controller == null) return;
        busy = true; UpdateButtons();
        string result;
        try { await operation(); result = success; }
        catch (Exception error) when (error is not OutOfMemoryException) { result = error.Message; }
        try { ShowState(await controller.ReadAsync()); }
        catch (Exception error) when (error is not OutOfMemoryException) { state = null; ProblemsText.Text = error.Message; }
        StatusText.Text = result;
        busy = false; UpdateButtons();
    }
    private async void RefreshClick(object sender, RoutedEventArgs e) => await ReadAsync();
    private async void ApplyPowerClick(object sender, RoutedEventArgs e)
    {
        if (PowerPicker.SelectedItem is Choice<LegionPowerMode> choice) await ApplyAsync(() => controller!.SetPowerAsync(choice.Value), "Power mode applied and verified.");
    }
    private async void ApplyChargeClick(object sender, RoutedEventArgs e)
    {
        if (ChargePicker.SelectedItem is Choice<LegionChargeMode> choice) await ApplyAsync(() => controller!.SetChargeAsync(choice.Value), "Charging mode applied and verified.");
    }
    private async void RestorePowerClick(object sender, RoutedEventArgs e) => await ApplyAsync(() => controller!.RestorePowerAsync(), "Original session power mode restored and verified.");
    private async void RestoreChargeClick(object sender, RoutedEventArgs e) => await ApplyAsync(() => controller!.RestoreChargeAsync(), "Original session charging mode restored and verified.");
    private async void ApplyLightingClick(object sender, RoutedEventArgs e)
    {
        if (KeyboardPicker.SelectedItem is not Choice<DeviceInfo> keyboard || EffectPicker.SelectedItem is not Choice<LegionFourZoneEffect> effect) return;
        byte[] colors;
        try { colors = new[] { Zone1.Text, Zone2.Text, Zone3.Text, Zone4.Text }.SelectMany(ParseColor).ToArray(); }
        catch (FormatException) { StatusText.Text = "Use #RRGGBB for each zone color."; return; }
        var report = LenovoLegionProtocol.FourZoneReport(effect.Value, (byte)(int)SpeedPicker.SelectedItem, (byte)(int)BrightnessPicker.SelectedItem, colors);
        await ApplyAsync(() => controller!.SetLightingAsync(keyboard.Value, report), "Lighting command sent. This controller does not provide color readback.");
    }
    private async void OffLightingClick(object sender, RoutedEventArgs e)
    {
        if (KeyboardPicker.SelectedItem is Choice<DeviceInfo> keyboard)
            await ApplyAsync(() => controller!.SetLightingAsync(keyboard.Value, LenovoLegionProtocol.FourZoneOffReport()), "Lighting-off command sent.");
    }
    private static byte[] ParseColor(string text)
    {
        text = text.Trim().TrimStart('#');
        if (text.Length != 6 || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) throw new FormatException();
        return [(byte)(value >> 16), (byte)(value >> 8), (byte)value];
    }
    private async void ApplySpectrumClick(object sender, RoutedEventArgs e)
    {
        if (SpectrumPicker.SelectedItem is Choice<LegionSpectrumState> choice && SpectrumProfile.SelectedItem is int profile && SpectrumBrightness.SelectedItem is int brightness)
            await ApplyAsync(() => controller!.SetSpectrumAsync(choice.Value.Device, new((byte)brightness, (byte)profile)), "Spectrum profile and brightness applied and verified.");
    }
    private async void ApplyGpuClick(object sender, RoutedEventArgs e)
    {
        if (GpuPicker.SelectedItem is Choice<LegionGpuMode> choice)
            await ApplyAsync(() => controller!.SetGpuAsync(choice.Value), "GPU firmware setting applied and verified. Active routing may require a restart.");
    }
    private async void RestoreGpuClick(object sender, RoutedEventArgs e) => await ApplyAsync(() => controller!.RestoreGpuAsync(), "Original GPU firmware setting restored. Active routing may require a restart.");
    private async void RestoreSpectrumClick(object sender, RoutedEventArgs e)
    {
        if (SpectrumPicker.SelectedItem is Choice<LegionSpectrumState> choice)
            await ApplyAsync(() => controller!.RestoreSpectrumAsync(choice.Value.Device), "Original Spectrum profile and brightness restored and verified.");
    }
}

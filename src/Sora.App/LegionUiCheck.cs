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
            await Task.Delay(200);
            window.UpdateLayout();
            Save(page, output + ".overview.png");
            var scroll = (ScrollViewer)page.Content;
            scroll.ScrollToEnd(); await Task.Delay(100); window.UpdateLayout();
            Save(page, output + ".lighting.png");
            checks["No fixture configuration was changed"] = transport.Writes == 0;
            await controller.StopAsync();
            var passed = checks.Values.All(value => value);
            using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            JsonSerializer.Serialize(stream, new { passed, synthetic = true, configurationWrites = 0, checks }, new JsonSerializerOptions { WriteIndented = true });
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
    private sealed class FixtureRecovery : ILegionRecoveryStore { public void Save(LegionRecovery recovery) => throw new InvalidOperationException("UI checks must not apply controls."); }
    private sealed class FixtureTransport : ILegionTransport, ILegionSpectrumTransport, ILegionGpuTransport
    {
        public int Writes { get; private set; }
        public LenovoSystemIdentity Identity => new("LENOVO", "82JQ", "Legion 5 Pro · UI fixture", "", "GKCN00WW", HardwarePresence.Present);
        public bool IsAcConnected() => true;
        public string[] Conflicts() => [];
        public LegionPowerMode[] ReadSupportedPowerModes() => [LegionPowerMode.Quiet, LegionPowerMode.Balanced, LegionPowerMode.Performance];
        public uint ReadPowerMode() => 2;
        public void WritePowerMode(uint value) { Writes++; throw new InvalidOperationException(); }
        public uint ChargeMode(uint request) { if (request == 255) return 0x20; Writes++; throw new InvalidOperationException(); }
        public DeviceInfo[] FourZoneDevices() => [new("ui-four-zone", "", 0xff89, 0xcc, 0, 33, 0x048d, 0xc985)];
        public void WriteFourZone(DeviceInfo device, byte[] report) { Writes++; throw new InvalidOperationException(); }
        public DeviceInfo[] SpectrumDevices() => [new("ui-spectrum", "", 0xff89, 7, 0, 960, 0x048d, 0xc987)];
        public LegionSpectrumSetting ReadSpectrum(DeviceInfo device) => new(6, 1);
        public void WriteSpectrum(DeviceInfo device, LegionSpectrumSetting setting) { Writes++; throw new InvalidOperationException(); }
        public LegionGpuState ReadGpu() => new(LegionGpuMode.Hybrid, Enum.GetValues<LegionGpuMode>());
        public void WriteGpu(LegionGpuMode mode) { Writes++; throw new InvalidOperationException(); }
    }
}

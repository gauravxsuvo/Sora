using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Sora.Core;

namespace Sora.App;

public partial class LogitechPanel
{
    private LogitechHardwareSettings? hardwareSettings;
    private bool HardwareSettingsDirty => hardwareSettings != null && (DpiAlwaysOnCheck.IsChecked != hardwareSettings.DpiAlwaysOn || StartupEffectCheck.IsChecked != hardwareSettings.StartupEffect);
    private async Task ReadHardwareSettingsAsync()
    {
        if (snapshot == null) return;
        hardwareSettings = await WithDeviceAsync(d => d.ReadSettingsAsync(snapshot));
        FirmwareText.Text = "Firmware: " + hardwareSettings.Firmware;
        DpiAlwaysOnCheck.IsChecked = hardwareSettings.DpiAlwaysOn; StartupEffectCheck.IsChecked = hardwareSettings.StartupEffect;
    }
    private async void ApplyDeviceSettingsClick(object sender, RoutedEventArgs e)
    {
        if (snapshot == null || hardwareSettings == null) return;
        var expected = hardwareSettings; var always = DpiAlwaysOnCheck.IsChecked == true; var startup = StartupEffectCheck.IsChecked == true;
        await RunAsync(async () =>
        {
            Directory.CreateDirectory(DataDirectory); var backup = Path.Combine(DataDirectory, "device-settings-original.json");
            if (!File.Exists(backup)) File.WriteAllText(backup, JsonSerializer.Serialize(expected, new JsonSerializerOptions { WriteIndented = true }));
            hardwareSettings = await WithDeviceAsync(async d =>
            {
                var profile = controls?.Profile ?? controlDraft;
                var level = profile == null ? (int?)null : LogitechDpiIndicator.Level(profile, await d.ReadLiveDpiAsync(snapshot));
                await d.WriteSettingsAsync(snapshot, expected, always, startup, level);
                return await d.ReadSettingsAsync(snapshot);
            });
            if (controls?.Running == true) await controls.RefreshIndicatorPolicyAsync();
            FirmwareText.Text = "Firmware: " + hardwareSettings.Firmware; EditStatus.Text = "Device preferences applied and verified.";
        }, "Applying device preferences…");
    }
}

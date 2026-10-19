using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sora.Core;

namespace Sora.App;

// Explicit interactive diagnostic. Captures only the first key in its own focused field.
internal static class KeyboardInputCheck
{
    internal static async Task<bool> RunAsync(string output, bool wireless = false)
    {
        KreoState? original = null;
        DeviceInfo? device = null;
        KreoReceiver? receiver = null;
        StreamWriter? trace = null;
        var model = KreoModel.All.Single(m => m.Name == "Swarm 75");
        string? received = null, error = null, restoreError = null;
        Window? window = null;
        var result = new TaskCompletionSource<Key>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (File.Exists(output) || File.Exists(output + ".backup.json")) throw new IOException("Choose a new output path to preserve earlier evidence.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        try
        {
            await Task.Run(async () =>
            {
                if (wireless)
                {
                    if (KreoKeyboard.FindConflict() is { } conflict) throw new IOException(conflict);
                    receiver = new KreoReceiver(KreoReceiver.Find() ?? throw new IOException("No Kreo receiver found."));
                    trace = new StreamWriter(output + ".trace.jsonl") { AutoFlush = true };
                    receiver.DiagnosticTrace += entry => trace.WriteLine(JsonSerializer.Serialize(entry));
                    original = (await receiver.ReadStateAsync(model)).State;
                    File.WriteAllText(output + ".backup.json", JsonSerializer.Serialize(original));
                    var wirelessTrial = original.Copy();
                    KreoProtocol.Remap(wirelessTrial, model.Keys.Single(k => k.Label == "F12").Index, KreoActions.ForVirtualKey(0x7a));
                    await receiver.ApplyAsync(original, wirelessTrial, model,
                        backup => File.WriteAllText(output + ".receiver-backup.json", JsonSerializer.Serialize(backup)));
                    return;
                }
                device = KreoKeyboard.Enumerate().Single();
                using var keyboard = new KreoKeyboard(device);
                original = keyboard.ReadState();
                File.WriteAllText(output + ".backup.json", JsonSerializer.Serialize(original));
                var trial = original.Copy();
                var source = keyboard.Definition.Keys.Single(k => k.Label == "F12");
                KreoProtocol.Remap(trial, source.Index, KreoActions.ForVirtualKey(0x7a));
                keyboard.Apply(original, trial, _ => { });
            });
            var panel = new StackPanel { Margin = new Thickness(24) };
            panel.Children.Add(new TextBlock { Text = "Press the physical F12 key once", FontSize = 22, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = "Sora temporarily mapped F12 to F11. This field will check what Windows receives, then restore your previous setup automatically.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 15, 0, 18) });
            var field = new TextBox { Text = "Click here, then press F12", IsReadOnly = true, Padding = new Thickness(10), FontSize = 17 };
            field.PreviewKeyDown += (_, e) => { result.TrySetResult(e.Key == Key.System ? e.SystemKey : e.Key); e.Handled = true; };
            panel.Children.Add(field);
            panel.Children.Add(new TextBlock { Text = "Restores automatically after 45 seconds, or if you close this window.", Margin = new Thickness(0, 15, 0, 0), TextWrapping = TextWrapping.Wrap });
            window = new Window { Title = "Sora · Physical keyboard check" + (wireless ? " · 2.4 GHz" : " · USB"), Content = panel, Width = 560, Height = 300, WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true };
            window.Closed += (_, _) => result.TrySetCanceled();
            window.Show(); window.Activate(); field.Focus();
            received = (await result.Task.WaitAsync(TimeSpan.FromSeconds(45))).ToString();
        }
        catch (Exception exception) { error = exception.Message; }
        finally
        {
            window?.Close();
            if (original != null && (device != null || receiver != null))
            {
                try
                {
                    await Task.Run(async () =>
                    {
                        if (receiver != null)
                        {
                            var current = (await receiver.ReadStateAsync(model)).State;
                            await receiver.ApplyAsync(current, original, model,
                                backup => File.WriteAllText(output + ".restore-backup.json", JsonSerializer.Serialize(backup)));
                            var restored = (await receiver.ReadStateAsync(model)).State;
                            if (!restored.Lighting.SequenceEqual(original.Lighting) || !restored.Keys.AsSpan(0, 504).SequenceEqual(original.Keys.AsSpan(0, 504)) ||
                                !restored.EffectPalette!.AsSpan(0, 490).SequenceEqual(original.EffectPalette!.AsSpan(0, 490)))
                                throw new IOException("Original receiver settings did not match final readback.");
                            return;
                        }
                        using var keyboard = new KreoKeyboard(device!);
                        keyboard.Apply(keyboard.ReadState(), original, _ => { });
                    });
                }
                catch (Exception exception) { restoreError = exception.ToString(); }
            }
            receiver?.Dispose(); trace?.Dispose();
            File.WriteAllText(output, JsonSerializer.Serialize(new { transport = wireless ? "2.4 GHz" : "USB", expectedKey = "F11", receivedKey = received, physicalMappingVerified = received == "F11", originalSetupRestored = original != null && restoreError == null, error, restoreError }, new JsonSerializerOptions { WriteIndented = true }));
        }
        return received == "F11" && restoreError == null;
    }
}

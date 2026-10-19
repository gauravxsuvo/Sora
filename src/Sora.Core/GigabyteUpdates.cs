using System.Globalization;
using System.Management;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Sora.Core;

public enum UpdateKind { Bios, Driver }

/// <summary>One newer download from Gigabyte for this PC. Url is always on <see cref="GigabyteUpdates.Host"/>.</summary>
public sealed record UpdateItem(UpdateKind Kind, string Name, string Description, string Installed, string Latest, string Url, long Size, string For);

/// <summary>What the check found. Board/BIOS identity is what Windows reports; Notes explain anything skipped.</summary>
public sealed record UpdateReport(string? Board, string? Bios, string? BoardSection, IReadOnlyList<UpdateItem> Updates, IReadOnlyList<UpdateItem> UpToDate, IReadOnlyList<string> Notes, DateTimeOffset CheckedAt)
{
    // A failed download, unmatched board or skipped driver must never become "up to date".
    public bool Incomplete => BoardSection == null || Notes.Count != 0;
    public string ResultLabel => Incomplete ? "Check incomplete" : Updates.Count == 0 ? "No updates found" : Updates.Count + (Updates.Count == 1 ? " update available" : " updates available");
    public string ResultDetail => Incomplete
        ? "Some BIOS or driver checks could not be completed. Review the notes; update status is not fully known." + (Updates.Count > 0 ? " Available matched downloads are listed below." : "")
        : Updates.Count == 0 ? "No newer matched BIOS or driver downloads were found. This check does not cover every device's firmware."
        : Updates.Count + (Updates.Count == 1 ? " matched update is" : " matched updates are") + " available. Download the ones you need, then review and run the official installer or BIOS workflow.";
    public string EmptyMessage => Incomplete ? "Update availability is unknown for incomplete checks. Review the notes below." : "No newer matched BIOS or driver downloads were found.";
}

/// <summary>A present device as GCC's Search_MB_Device_Class sees it: IDs from its first hardware ID, version from its driver key.</summary>
public sealed record UpdateDevice(string Name, string HardwareId, uint DevId, uint SubId, string DriverVersion);

/// <summary>
/// GCC's motherboard update check (GBT_MB_Update / MB_Update_Lib 26.x, decompiled), reproduced read-only. It downloads the same
/// public index files from Gigabyte (BIOS/ver2.ini; Drvdata/ver2.ini and its CSV tables), matches them to this PC exactly as GCC
/// does, and is stricter where GCC could offer something wrong:
/// <list type="bullet">
/// <item>BIOS: the board must be a Gigabyte board whose name matches exactly one BIOS.ini section (GCC uses the BIOS tag from an
/// ACPI method instead; if a name ever matches several revisions, Sora offers no BIOS rather than guess).</item>
/// <item>Drivers: only for devices that are present with a known installed version (GCC also offers drivers it cannot find
/// installed, "N/A"), compared part by part with GCC's rule (same number of parts, strictly greater).</item>
/// <item>Every link must be HTTPS on mb.download.gigabyte.com and answer with a size, as GCC requires.</item>
/// </list>
/// Nothing is installed or flashed. GCC's own modules and its advertised apps (Norton) are left out: Sora replaces GCC.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class GigabyteUpdates
{
    public const string Host = "mb.download.gigabyte.com";
    public const string SoftwareRoot = "https://mb.download.gigabyte.com/FileList/Swhttp/DriverUpd/";
    public const string BiosRoot = "https://mb.download.gigabyte.com/FileList/BIOS/";

    public static async Task<UpdateReport> CheckAsync(HttpClient http, CancellationToken token = default)
    {
        var notes = new List<string>();
        var updates = new List<UpdateItem>();
        var upToDate = new List<UpdateItem>();
        var (manufacturer, board, bios) = BoardIdentity();
        if (board == null || manufacturer?.Contains("Gigabyte", StringComparison.OrdinalIgnoreCase) != true)
            return new(board, bios, null, [], [], ["This is not a Gigabyte motherboard (Windows reports \"" + (manufacturer ?? "unknown") + "\"), so Gigabyte's updates do not apply."], DateTimeOffset.Now);

        string? section = null;
        try
        {
            var ini = ParseIni(await GetTextAsync(http, SoftwareRoot + "BIOS/ver2.ini", token));
            var (found, item, note) = await BiosAsync(http, ini, board, bios, token);
            section = found;
            if (note != null) notes.Add(note);
            if (item != null) (CompareBios(item.Latest, item.Installed) > 0 ? updates : upToDate).Add(item);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or TaskCanceledException or InvalidDataException)
        { notes.Add("BIOS check failed: " + error.Message); }

        try
        {
            if (PackageFile(board) is not { } package)
                notes.Add("Gigabyte's driver tables have no package list for a " + board + ", so drivers were not checked.");
            else
            {
                var path = ParseIni(await GetTextAsync(http, SoftwareRoot + "Drvdata/ver2.ini", token)).GetValueOrDefault("Info")?.GetValueOrDefault("path");
                if (path == null || !Regex.IsMatch(path, "^[0-9]{6,12}$")) throw new InvalidDataException("Gigabyte's driver index has no valid data version.");
                async Task<List<string[]>> Table(string name) => ParseCsv(await GetTextAsync(http, SoftwareRoot + "Drvdata/" + path + "/" + name, token));
                var tables = new DriverTables(await Table("DriverDesp.csv"), await Table("DriverTable.csv"), await Table("IDtoDriver.csv"), await Table("PCITable.csv"), await Table(package));
                foreach (var (item, newer) in Match(tables, Devices(), Environment.OSVersion.Version.Build, diagnostic: notes.Add))
                {
                    var size = await SizeAsync(http, item.Url, token);
                    if (size <= 0) { notes.Add(item.Name + " " + item.Latest + ": Gigabyte's file is not available, so it is not offered."); continue; }
                    (newer ? updates : upToDate).Add(item with { Size = size });
                }
            }
        }
        catch (Exception error) when (error is HttpRequestException or IOException or TaskCanceledException or InvalidDataException or ManagementException or COMException)
        { notes.Add("Driver check failed: " + error.Message); }

        return new(board, bios, section, Dedupe(updates), Dedupe(upToDate), notes, DateTimeOffset.Now);
    }

    /// <summary>One entry per download (several devices can map to the same driver package, as in GCC's list).</summary>
    private static IReadOnlyList<UpdateItem> Dedupe(List<UpdateItem> items) =>
        items.GroupBy(i => i.Url, StringComparer.OrdinalIgnoreCase).Select(g => g.First() with { For = string.Join("; ", g.Select(i => i.For).Distinct()) })
            .OrderBy(i => i.Kind).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    // ---------------- BIOS ----------------

    private static async Task<(string? Section, UpdateItem? Item, string? Note)> BiosAsync(HttpClient http, Dictionary<string, Dictionary<string, string>> ini, string board, string? installed, CancellationToken token)
    {
        var matches = ini.Where(s => s.Value.TryGetValue("NAME", out var name) && SameBoard(name, board)).ToArray();
        if (matches.Length == 0) return (null, null, "Gigabyte's BIOS list has no entry for " + board + ".");
        if (matches.Length > 1) return (null, null, "Gigabyte lists several revisions of " + board + " (" + string.Join(", ", matches.Select(m => m.Value["NAME"])) + "); Sora will not guess which one this is. Check the revision printed on the board and use Gigabyte's support page.");
        var (part, values) = (matches[0].Key, matches[0].Value);
        if (!Regex.IsMatch(part, "^[0-9A-Za-z]{6,12}$") || !values.TryGetValue("BIOSVer0", out var latest) || !Regex.IsMatch(latest, "^F[0-9]{1,3}[a-z]?$", RegexOptions.IgnoreCase))
            return (part, null, "Gigabyte's BIOS entry for " + board + " is not in the expected format, so no BIOS is offered.");
        if (installed == null || !Regex.IsMatch(installed, "^F[0-9]{1,3}[a-z]?$", RegexOptions.IgnoreCase))
            return (part, null, "The installed BIOS version (" + (installed ?? "unknown") + ") is not a Gigabyte release number, so it cannot be compared.");
        // GCC's file name: mb_bios_<model with dashes>_<part>_<version>.zip, all lower case.
        var url = BiosRoot + "mb_bios_" + board.Replace(' ', '-').ToLowerInvariant() + "_" + part.ToLowerInvariant() + "_" + latest.ToLowerInvariant() + ".zip";
        var size = await SizeAsync(http, url, token);
        if (size <= 0) return (part, null, "BIOS " + latest + " is listed for " + board + " but Gigabyte's file is not available yet.");
        return (part, new UpdateItem(UpdateKind.Bios, board, values.GetValueOrDefault("0") ?? "", installed.ToUpperInvariant(), latest.ToUpperInvariant(), url, size, values["NAME"]), null);
    }

    /// <summary>"Z890 GAMING X WIFI7(rev.1.x)" names the board "Z890 GAMING X WIFI7"; the comparison is exact apart from case and spacing.</summary>
    public static bool SameBoard(string iniName, string board)
    {
        static string Norm(string text) => Regex.Replace(Regex.Replace(text, @"\(\s*rev\.?[^)]*\)\s*$", "", RegexOptions.IgnoreCase), @"\s+", " ").Trim().ToUpperInvariant();
        return Norm(iniName).Length > 0 && Norm(iniName) == Norm(board);
    }

    /// <summary>Gigabyte BIOS numbers: F20 &lt; F20a &lt; F21 (GCC compares them as hex, which misorders F9a and F10).</summary>
    public static int CompareBios(string a, string b)
    {
        static (int Number, string Letter) Parse(string v) { var m = Regex.Match(v.ToUpperInvariant(), "^F([0-9]+)([A-Z]?)$"); return m.Success ? (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Value) : (-1, ""); }
        var (x, y) = (Parse(a), Parse(b));
        return x.Number != y.Number ? x.Number.CompareTo(y.Number) : string.CompareOrdinal(x.Letter, y.Letter);
    }

    // ---------------- Drivers ----------------

    public sealed record DriverTables(List<string[]> Desp, List<string[]> Driver, List<string[]> IdToDriver, List<string[]> Pci, List<string[]> Package);

    /// <summary>GCC's GetMbPlatformId → package file, for the Intel 600/700/800/W790 and AMD families GCC knows.</summary>
    public static string? PackageFile(string board)
    {
        bool Has(params string[] chips) => chips.Any(c => board.Contains(c, StringComparison.OrdinalIgnoreCase));
        if (Has("Z890", "B860", "H810", "Q870", "W880")) return "Package-Intel-800.csv";
        if (Has("Z790", "B760")) return "Package-Intel-700.csv";
        if (Has("Z690", "B660", "H610")) return "Package-Intel-600.csv";
        if (Has("W790")) return "Package-Intel-W790.csv";
        if (Has("X870", "B850", "B840", "X670", "B650", "A620", "TRX50", "A520", "B550", "X570", "X470", "B450")) return "Package-AMD.csv";
        return null;
    }

    /// <summary>
    /// GCC's Get_Update_ListS for each device: first PCITable row that matches → IDtoDriver rows for this Windows → DriverTable row;
    /// the installed version comes from the device's driver, or from the registry for package drivers (Package-*.csv).
    /// Returns every confirmed match with whether it is newer. Items without a known installed version are not returned.
    /// </summary>
    public static IEnumerable<(UpdateItem Item, bool Newer)> Match(DriverTables tables, IEnumerable<UpdateDevice> devices, int windowsBuild, Func<string, string?>? registry = null, Action<string>? diagnostic = null)
    {
        registry ??= RegistryVersion;
        var osColumn = windowsBuild >= 22000 ? 3 : 16; // Win1164 or W1064 in IDtoDriver.csv, as GCC's Get_Os_Type.
        foreach (var device in devices)
        {
            var pciNum = tables.Pci.Skip(1).Select(row => PciTableId(row, device.DevId, device.SubId)).FirstOrDefault(id => id >= 0, -1);
            if (pciNum < 0) continue;
            foreach (var row in tables.IdToDriver.Skip(1))
            {
                if (row.Length <= osColumn || Int(row, 1) != pciNum || row[osColumn].Trim() != "1" || Int(row, 2) is not { } driverNum) continue;
                var driver = tables.Driver.Skip(1).FirstOrDefault(d => Int(d, 0) == driverNum);
                if (driver == null || driver.Length < 14) continue;
                var latest = driver[13].Trim();
                var package = tables.Package.Skip(1).FirstOrDefault(p => Int(p, 0) == driverNum);
                var installed = Int(driver, 5) switch
                {
                    99 => package is { Length: >= 6 } ? registry("key|" + package[3] + "|" + package[4] + "|" + package[5]) : null,
                    98 => package is { Length: >= 4 } ? registry("search|" + package[3] + "|" + package[1]) : null,
                    _ => device.DriverVersion,
                };
                // A package can be associated with a different device (e.g. Bluetooth via a Wi-Fi PCI ID).
                // Missing package metadata must never fall back to that device's unrelated driver version.
                if (string.IsNullOrWhiteSpace(installed) || installed is "N/A" or "0.0.0.0" || !IsVersion(installed.Trim()) || !IsVersion(latest) || installed.Trim().Split('.').Length != latest.Split('.').Length)
                {
                    diagnostic?.Invoke("Driver package " + driverNum + " for " + device.Name + ": installed package version is unknown or cannot be compared; review the official support page.");
                    continue;
                }
                var url = DriverUrl(driver);
                if (url == null) continue;
                var desp = tables.Desp.Skip(1).FirstOrDefault(d => Int(d, 0) == Int(driver, 2));
                var name = desp?.ElementAtOrDefault(1)?.Trim() is { Length: > 0 } n ? n : "Driver " + driverNum;
                yield return (new UpdateItem(UpdateKind.Driver, name, desp is { Length: > 2 } ? string.Join(",", desp.Skip(2)).Trim() : "", installed.Trim(), latest, url, 0, device.Name), Newer(latest, installed.Trim()));
            }
        }
    }

    /// <summary>GCC's Check_Version: both versions must have the same number of parts; newer when strictly greater part by part.</summary>
    public static bool Newer(string latest, string installed)
    {
        var (a, b) = (latest.Split('.'), installed.Split('.'));
        if (a.Length != b.Length || a.Length is < 1 or > 4) return false;
        for (var i = 0; i < a.Length; i++)
        {
            if (!int.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out var x) || !int.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out var y)) return false;
            if (x != y) return x > y;
        }
        return false;
    }

    private static bool IsVersion(string text) => Regex.IsMatch(text, @"^[0-9]+(\.[0-9]+){0,3}$");

    /// <summary>GCC's Get_Driver_Url: path + number_version_file, upper case, under DriverUpd. Refused if any part could leave that folder.</summary>
    public static string? DriverUrl(string[] driver)
    {
        var relative = (driver[3] + driver[0] + "_" + driver[13] + "_" + driver[4]).Replace('\\', '/').Trim().ToUpperInvariant();
        if (relative.Contains("..") || relative.StartsWith('/') || relative.Contains(':') || !Regex.IsMatch(relative, @"^[A-Z0-9_./ ()&+-]+$")) return null;
        return SoftwareRoot + relative;
    }

    /// <summary>GCC's Get_PCI_Table_ID: ispeccase 0 matches the vendor/device ID alone, otherwise the subsystem must match too.</summary>
    public static int PciTableId(string[] row, uint devId, uint subId)
    {
        if (row.Length < 6 || Hex(row[1]) is not { } id || id != devId) return -1;
        if (Int(row, 2) == 0) return Int(row, 5) ?? -1;
        return row[3].Trim().Length > 0 && Hex(row[3]) == subId ? Int(row, 5) ?? -1 : -1;
    }

    private static int? Int(string[] row, int index) => index < row.Length && int.TryParse(row[index].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    private static uint? Hex(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t[2..];
        return uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    /// <summary>
    /// GCC's IDs from a hardware ID: PCI/HDAUDIO/ACPI "…VEN_vvvv&amp;DEV_dddd…" → 0xddddvvvv (just dddd when the vendor is not hex, e.g.
    /// ACPI\VEN_ITE&amp;DEV_8851 → 0x8851), SUBSYS_ssssssss; USB/HID VID → id, PID → sub. Null for other buses.
    /// </summary>
    public static (uint DevId, uint SubId)? ParseHardwareId(string id)
    {
        var upper = id.ToUpperInvariant();
        if (upper.StartsWith("USB\\VID_") || upper.StartsWith("HID\\VID_"))
        {
            var vid = Regex.Match(upper, "VID_([0-9A-F]{4})"); var pid = Regex.Match(upper, "&PID_([0-9A-F]{4})");
            return vid.Success ? (Convert.ToUInt32(vid.Groups[1].Value, 16), pid.Success ? Convert.ToUInt32(pid.Groups[1].Value, 16) : 0) : null;
        }
        if (!(upper.StartsWith("PCI\\VEN_") || upper.StartsWith("HDAUDIO\\FUNC_") || upper.StartsWith("ACPI\\VEN_")) || upper.StartsWith("ACPI\\VEN_PNP&DEV_0000")) return null;
        var at = upper.IndexOf("&DEV_", StringComparison.Ordinal);
        if (at < 4 || at + 9 > upper.Length) return null;
        var text = upper.Substring(at + 5, 4) + upper.Substring(at - 4, 4);
        var hexLength = text.TakeWhile(Uri.IsHexDigit).Count();
        if (hexLength < text.Length - 1) text = text[..4];
        if (!uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var dev)) return null;
        var sub = Regex.Match(upper, "&SUBSYS_([0-9A-F]{8})");
        return (dev, sub.Success ? Convert.ToUInt32(sub.Groups[1].Value, 16) : 0);
    }

    /// <summary>Present devices with their first hardware ID and installed driver version (read-only WMI and registry).</summary>
    public static IReadOnlyList<UpdateDevice> Devices()
    {
        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var drivers = new ManagementObjectSearcher("SELECT DeviceID, DriverVersion FROM Win32_PnPSignedDriver"))
            foreach (var d in drivers.Get().Cast<ManagementBaseObject>())
                if (d["DeviceID"] is string id && d["DriverVersion"] is string version && version.Length > 0) versions[id] = version;
        var devices = new List<UpdateDevice>();
        // Present devices only, as GCC's SetupDiGetClassDevs(DIGCF_PRESENT | DIGCF_ALLCLASSES); error 45 is "not connected".
        using var searcher = new ManagementObjectSearcher("SELECT DeviceID, Name, HardwareID, ConfigManagerErrorCode FROM Win32_PnPEntity");
        foreach (var entity in searcher.Get().Cast<ManagementBaseObject>())
        {
            if (entity["ConfigManagerErrorCode"] is uint code && code == 45) continue;
            if (entity["HardwareID"] is not string[] { Length: > 0 } ids || ParseHardwareId(ids[0]) is not { } parsed) continue;
            var deviceId = entity["DeviceID"] as string ?? "";
            devices.Add(new(entity["Name"] as string ?? ids[0], ids[0], parsed.DevId, parsed.SubId, versions.GetValueOrDefault(deviceId) ?? "0.0.0.0"));
        }
        return devices;
    }

    /// <summary>"key|view|path|name" reads one value; "search|view|DisplayName" finds an Uninstall entry's DisplayVersion (top level, as GCC).</summary>
    private static string? RegistryVersion(string query)
    {
        try
        {
            var parts = query.Split('|');
            var view = parts[1].Trim() == "512" ? RegistryView.Registry32 : parts[1].Trim() == "256" ? RegistryView.Registry64 : RegistryView.Default;
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            if (parts[0] == "key" && parts.Length == 4)
                return root.OpenSubKey(parts[2].Replace(@"\\", @"\"))?.GetValue(parts[3]) as string;
            if (parts[0] == "search" && parts.Length == 3)
            {
                using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                foreach (var name in uninstall?.GetSubKeyNames() ?? [])
                {
                    using var key = uninstall!.OpenSubKey(name);
                    if (key?.GetValue("DisplayName") as string == parts[2]) return key.GetValue("DisplayVersion") as string;
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return null;
    }

    // ---------------- Board, files, parsing ----------------

    public static (string? Manufacturer, string? Board, string? Bios) BoardIdentity()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        return ((key?.GetValue("BaseBoardManufacturer") as string)?.Trim(), (key?.GetValue("BaseBoardProduct") as string)?.Trim(), (key?.GetValue("BIOSVersion") as string)?.Trim());
    }

    /// <summary>Index files: UTF-16 with a BOM (BIOS list) or plain text. Only from Gigabyte's download host.</summary>
    private static async Task<string> GetTextAsync(HttpClient http, string url, CancellationToken token)
    {
        RequireHost(url);
        var bytes = await http.GetByteArrayAsync(url, token);
        if (bytes.Length > 8_000_000) throw new InvalidDataException("Gigabyte's index file is unexpectedly large.");
        if (bytes is [0xFF, 0xFE, ..]) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes is [0xEF, 0xBB, 0xBF, ..]) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Size from a HEAD request, as GCC's Get_File_Header_VIA_HTTP; 0 when the file is missing.</summary>
    private static async Task<long> SizeAsync(HttpClient http, string url, CancellationToken token)
    {
        RequireHost(url);
        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        using var response = await http.SendAsync(request, token);
        return response.IsSuccessStatusCode ? response.Content.Headers.ContentLength ?? 0 : 0;
    }

    public static void RequireHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Refused a download that is not from " + Host + ".");
    }

    /// <summary>
    /// Downloads one update to <paramref name="folder"/>, checks it arrived from Gigabyte's host at the advertised size, and returns
    /// the file. A partial file is removed. Nothing is run.
    /// </summary>
    public static async Task<string> DownloadAsync(HttpClient http, UpdateItem item, string folder, IProgress<double>? progress, CancellationToken token)
    {
        RequireHost(item.Url);
        var name = Path.GetFileName(new Uri(item.Url).AbsolutePath);
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new InvalidDataException("Gigabyte's file name is not valid.");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, name);
        var partial = target + ".part";
        try
        {
            using var response = await http.GetAsync(item.Url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            RequireHost(response.RequestMessage?.RequestUri?.ToString() ?? item.Url);
            var total = response.Content.Headers.ContentLength ?? item.Size;
            await using (var source = await response.Content.ReadAsStreamAsync(token))
            await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
            {
                var buffer = new byte[1 << 16];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, token)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), token);
                    done += read;
                    if (total > 0) progress?.Report((double)done / total);
                }
                if (done != total) throw new IOException($"The download is incomplete ({done:N0} of {total:N0} bytes).");
                if (item.Size > 0 && done != item.Size) throw new IOException($"The file is {done:N0} bytes, but Gigabyte listed {item.Size:N0}; it was not kept.");
            }
            File.Move(partial, target, true);
            return target;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }

    public static Dictionary<string, Dictionary<string, string>> ParseIni(string text)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? current = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0 || line[0] == ';') continue;
            if (line[0] == '[' && line.EndsWith(']')) { var name = line[1..^1].Trim(); current = result.TryGetValue(name, out var existing) ? existing : result[name] = new(StringComparer.OrdinalIgnoreCase); continue; }
            var eq = line.IndexOf('=');
            if (current != null && eq > 0) current.TryAdd(line[..eq].Trim(), line[(eq + 1)..].Trim());
        }
        return result;
    }

    /// <summary>GCC's tables are plain comma-separated rows without quoting.</summary>
    public static List<string[]> ParseCsv(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).Select(l => l.Split(',')).ToList();
}

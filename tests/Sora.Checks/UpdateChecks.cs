using Sora.Core;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class UpdateChecks
{
    internal static void Run(Action<bool, string> check)
    {
        // Board identity: exact name only; revisions in parentheses are ignored, other models never match.
        check(GigabyteUpdates.SameBoard("Z890 GAMING X WIFI7(rev.1.x)", "Z890 GAMING X WIFI7") && GigabyteUpdates.SameBoard("z890  gaming x wifi7 (rev. 1.0)", "Z890 GAMING X WIFI7"), "BIOS entry matches the exact board name");
        check(!GigabyteUpdates.SameBoard("Z890 GAMING X WIFI7 ICE(rev.1.x)", "Z890 GAMING X WIFI7") && !GigabyteUpdates.SameBoard("Z890 GAMING X(rev.1.x)", "Z890 GAMING X WIFI7") && !GigabyteUpdates.SameBoard("Z890 AORUS ELITE WIFI7(rev.1.x)", "Z890 GAMING X WIFI7"), "BIOS entries of other models never match");
        check(GigabyteUpdates.CompareBios("F21", "F20") > 0 && GigabyteUpdates.CompareBios("F20a", "F20") > 0 && GigabyteUpdates.CompareBios("F10", "F9a") > 0 && GigabyteUpdates.CompareBios("F20", "F20") == 0 && GigabyteUpdates.CompareBios("F3", "F20") < 0, "BIOS numbers compare F9a < F10 < F20 < F20a < F21");
        // Driver versions: GCC's rule, same number of parts and strictly greater.
        check(GigabyteUpdates.Newer("32.0.100.4404", "32.0.100.3053") && !GigabyteUpdates.Newer("10.1.2.9", "10.1.2.9") && !GigabyteUpdates.Newer("18.4032.2510.0901", "6102.24.143.0") && !GigabyteUpdates.Newer("2.3", "2.3.0.0") && !GigabyteUpdates.Newer("1.x", "1.0"), "Driver versions: strictly newer, same shape, numeric only");
        // Hardware IDs exactly as GCC's Search_MB_Device_Class parses them.
        check(GigabyteUpdates.ParseHardwareId(@"PCI\VEN_8086&DEV_7D1D&SUBSYS_D0001458&REV_04") == (0x7D1D8086u, 0xD0001458u), "PCI ID is device+vendor with subsystem");
        check(GigabyteUpdates.ParseHardwareId(@"ACPI\VEN_ITE&DEV_8851") == (0x8851u, 0u) && GigabyteUpdates.ParseHardwareId(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0897&SUBSYS_1458A194&REV_1000") == (0x089710ECu, 0x1458A194u), "ACPI vendor names and HD Audio IDs parse like GCC");
        check(GigabyteUpdates.ParseHardwareId(@"USB\VID_0BDA&PID_8922&REV_0000") == (0x0BDAu, 0x8922u) && GigabyteUpdates.ParseHardwareId(@"ROOT\SOMETHING") == null && GigabyteUpdates.ParseHardwareId(@"ACPI\VEN_PNP&DEV_0000") == null, "USB IDs parse; other buses are ignored");
        check(GigabyteUpdates.PciTableId(["x", "0x7D1D8086", "0", "", "1", "500"], 0x7D1D8086, 0) == 500 && GigabyteUpdates.PciTableId(["x", "0x7D1D8086", "1", "0xD0001458", "1", "501"], 0x7D1D8086, 0x12341458) == -1, "Special-case PCI rows also require the subsystem");
        // Download links stay under Gigabyte's DriverUpd folder.
        string[] Driver(string path, string file) => ["3694", "0", "1", path, file, "0", "0", "1", "1", "0", "0", "0", "0", "32.0.100.4404"];
        check(GigabyteUpdates.DriverUrl(Driver("Chipset/800SER/NPU/", "DCHsetup.exe")) == GigabyteUpdates.SoftwareRoot + "CHIPSET/800SER/NPU/3694_32.0.100.4404_DCHSETUP.EXE", "Driver link is GCC's upper-case path");
        check(GigabyteUpdates.DriverUrl(Driver("../../evil/", "x.exe")) == null && GigabyteUpdates.DriverUrl(Driver("https://evil.example/", "x.exe")) == null, "Driver links cannot leave Gigabyte's folder");
        var refused = 0;
        foreach (var url in new[] { "http://mb.download.gigabyte.com/x", "https://evil.example/x", "https://mb.download.gigabyte.com.evil.example/x" })
            try { GigabyteUpdates.RequireHost(url); } catch (InvalidDataException) { refused++; }
        check(refused == 3, "Only HTTPS downloads from mb.download.gigabyte.com are allowed");
        // A full match through the tables: present device with an older driver → offered; same version → not newer; unknown → skipped.
        var tables = new GigabyteUpdates.DriverTables(
            [["Num", "Driver_Desp", "Driver_Tip"], ["7", "Intel NPU Driver", "NPU, with commas"]],
            [["Num", "Item", "Desp", "Path", "Exe", "Ver", "Opt", "Run", "W11", "", "", "", "", "Current"], ["3694", "0", "7", "Chipset/800SER/NPU/", "DCHsetup.exe", "0", "0", "1", "1", "0", "0", "0", "0", "32.0.100.4404"]],
            [["Num", "ID_num", "Driver_Num", "Win1164"], ["1", "900", "3694", "1"]],
            [["szPCIInfo", "VendorDeviceID", "ispeccase", "SubSystemID", "Item", "Num"], ["npu", "0x7D1D8086", "0", "", "1", "900"]],
            [["DriverID", "DisplayName", "PackageType", "View", "Path", "Name"]]);
        UpdateDevice Npu(string version) => new("Intel(R) AI Boost", @"PCI\VEN_8086&DEV_7D1D", 0x7D1D8086, 0, version);
        var older = GigabyteUpdates.Match(tables, [Npu("32.0.100.3053")], 26300).ToArray();
        check(older.Length == 1 && older[0].Newer && older[0].Item.Description == "NPU, with commas" && older[0].Item.For == "Intel(R) AI Boost", "An older installed driver is offered for the device it belongs to");
        check(GigabyteUpdates.Match(tables, [Npu("32.0.100.4404")], 26300).Single().Newer == false, "The same version is not offered");
        check(!GigabyteUpdates.Match(tables, [Npu("0.0.0.0")], 26300).Any() && !GigabyteUpdates.Match(tables, [Npu("32.0.100.3053") with { DevId = 0x7D1E8086 }], 26300).Any(), "Unknown versions and other devices are never offered");
        check(!GigabyteUpdates.Match(tables, [Npu("32.0.100.3053")], 19045).Any(), "Drivers flagged for another Windows version are not offered");
        // Real GCC metadata maps package installers via another device and can omit their package registry rows.
        // The PCI device's driver version is not the missing package's installed version.
        foreach (var mode in new[] { "98", "99" })
        {
            var packageDriver = (string[])tables.Driver[1].Clone(); packageDriver[5] = mode;
            var packageTables = tables with { Driver = new() { tables.Driver[0], packageDriver } };
            var notes = new List<string>();
            check(!GigabyteUpdates.Match(packageTables, [Npu("32.0.100.3053")], 26300, _ => throw new Exception("Missing package row must not access the registry"), notes.Add).Any() && notes.Count == 1,
                "Missing package metadata stays unknown instead of using an unrelated device version");
            packageTables = packageTables with { Package = new() { tables.Package[0], new[] { "3694", "Test Package", "", "256", "SOFTWARE\\Test", "Version" } } };
            check(!GigabyteUpdates.Match(packageTables, [Npu("32.0.100.3053")], 26300, _ => null).Any(), "Missing installed package version cannot fall back to device driver");
            var known = GigabyteUpdates.Match(packageTables, [Npu("99.0.0.0")], 26300, _ => "32.0.100.3053").Single();
            check(known.Newer && known.Item.Installed == "32.0.100.3053", "Known package version comes from its own registry record");
            packageTables = packageTables with { Package = new() { tables.Package[0], new[] { "3694", "Test Package" } } };
            check(!GigabyteUpdates.Match(packageTables, [Npu("32.0.100.3053")], 26300, _ => "32.0.100.3053").Any(), "Truncated package metadata is refused without out-of-range access");
        }
        var incompatibleNotes = new List<string>();
        check(!GigabyteUpdates.Match(tables, [Npu("32.0")], 26300, diagnostic: incompatibleNotes.Add).Any() && incompatibleNotes.Count == 1, "Incomparable version shapes stay unknown rather than up to date");
        check(GigabyteUpdates.PackageFile("Z890 GAMING X WIFI7") == "Package-Intel-800.csv" && GigabyteUpdates.PackageFile("X870E AORUS MASTER") == "Package-AMD.csv" && GigabyteUpdates.PackageFile("H470M DS3H") == null, "Platform package files follow GCC's board families");
    }
}

using Sora.Core;

static class HardwareDiscoveryChecks
{
    public static void Run(Action<bool, string> require)
    {
        var empty = HardwareDiscovery.Classify([], "", "");
        require(empty.Modules.Length == Enum.GetValues<HardwareModule>().Length && empty.Modules.All(m => m.Presence == HardwarePresence.Absent), "Empty machine has no device modules");
        require(!HardwareInventory.Unknown.Available(HardwareModule.Cooler) && HardwareInventory.Unknown.Presence(HardwareModule.Cooler) == HardwarePresence.Unknown, "Unknown discovery is distinct from absent and cannot enable output");

        var mouseOnly = HardwareDiscovery.Classify([new("Logitech G102 LIGHTSYNC Gaming Mouse", "HIDClass", @"HID\VID_046D&PID_C092")], "", "");
        require(mouseOnly.Available(HardwareModule.Logitech) && !mouseOnly.Available(HardwareModule.Cooler) && !mouseOnly.Available(HardwareModule.MotherboardFans), "G102-only machine exposes no cooler or motherboard controls");
        var noBoard = HardwareDiscovery.WithoutBoardIdentity([new("Cooler", "HIDClass", @"USB\VID_3633&PID_000A"), new("G102", "Mouse", @"HID\VID_046D&PID_C092")]);
        require(noBoard.Presence(HardwareModule.MotherboardFans) == HardwarePresence.DetectionFailed && noBoard.Available(HardwareModule.Cooler) && noBoard.Available(HardwareModule.Logitech), "Unavailable board identity cannot hide independently detected peripherals");
        var board = HardwareDiscovery.Classify([], "Gigabyte Technology Co., Ltd.", "Z890 GAMING X WIFI7");
        require(board.Available(HardwareModule.MotherboardFans) && board.Available(HardwareModule.Lighting) && !board.Available(HardwareModule.Cooler), "Motherboard model cannot imply an installed cooler");
        require(!HardwareDiscovery.Classify([], "Other", "Z890 GAMING X WIFI7").Available(HardwareModule.MotherboardFans), "Board match requires manufacturer");
        require(!HardwareDiscovery.Classify([], "Gigabyte", "Z890 UNKNOWN").Available(HardwareModule.MotherboardFans), "Unqualified board models do not get fan controls");
        var mixed = HardwareDiscovery.Classify([
            new("Display", "HIDClass", @"USB\VID_3633&PID_000A"),
            new("Receiver", "HIDClass", @"USB\VID_3554&PID_FA09"),
            new("GPU", "Display", @"PCI\VEN_10DE&DEV_0000"),
            new("Disk", "DiskDrive", @"SCSI\DiskTEST"),
            new("Wireless adapter", "Net", @"PCI\VEN_1234&DEV_0001")], "", "");
        foreach (var module in new[] { HardwareModule.Cooler, HardwareModule.Keyboard, HardwareModule.GpuFans, HardwareModule.Storage, HardwareModule.Wifi })
            require(mixed.Available(module), "Mixed-machine discovery: " + module);
        require(!HardwareDiscovery.Classify([new("Ethernet", "Net", @"PCI\VEN_10DE&DEV_0000")], "", "").Available(HardwareModule.GpuFans), "NVIDIA network adapter cannot imply GPU controls");
        require(!HardwareDiscovery.Classify([new("Logitech keyboard", "Keyboard", @"HID\VID_046D&PID_FFFF")], "", "").Available(HardwareModule.Logitech), "Logitech keyboard does not expose a mouse page");

        DeviceInfo Mouse(ushort pid, string path, string name = "") => new(path, "", 0xff00, 2, 20, VendorId: 0x046d, ProductId: pid, ProductName: name);
        foreach (var pid in new ushort[] { 0xc084, 0xc092, 0xc09d, 0xc08b })
            require(LogitechModels.IsConfigurationInterface(Mouse(pid, "mouse")), "Known wired mouse configuration interface accepted: " + pid);
        require(!LogitechModels.IsConfigurationInterface(Mouse(0xffff, "mouse")), "Unknown Logitech protocols are not opened");
        require(!LogitechModels.IsConfigurationInterface(Mouse(0xc092, "mouse") with { VendorId = 0x1234 }), "Product ID from another vendor rejected");
        require(!LogitechModels.IsConfigurationInterface(Mouse(0xc092, "mouse") with { OutputReportLength = 7 }), "Short HID peer is not a separate mouse");
        var a = Mouse(0xc092, @"hid#vid_046d&pid_c092&col02#port-a&0001#{test}", "Logitech G102 LIGHTSYNC");
        var duplicate = a with { Path = a.Path.ToUpperInvariant() };
        var b = a with { Path = a.Path.Replace("port-a", "port-b") };
        require(LogitechModels.ConfigurationInterfaces([a, duplicate, b]).Length == 2, "Duplicate descriptors grouped but identical serial-less mice stay separate");
        require(LogitechModels.Name(a) == "Logitech G102 LIGHTSYNC", "Real product string takes precedence over family fallback");
        require(LogitechModels.Name(Mouse(0xc09d, "mouse")).Contains("G102"), "Variant without a product string still has its correct family");
        var features = new ushort[] { 3, 0x2201, 0x8060, 0x8100, 0x8110, 0x1300, 0x8070 }.Select((id, i) => new LogitechFeature(id, (byte)i, 0, 0)).ToArray();
        var snapshot = new LogitechSnapshot("Test", "synthetic", 0xc08b, "2.0", features, [new(0, 800, 800, 100, 8000, 50, [])], 1000, [1000], 1, []);
        require(LogitechModels.HasG502Controls(snapshot) && LogitechModels.HasG502Preferences(snapshot), "Qualified G502 retains controls");
        require(!LogitechModels.HasG502Controls(snapshot with { ProductId = 0xc092 }) && !LogitechModels.HasG502Preferences(snapshot with { ProductId = 0xc092 }), "G102 cannot acquire G502-only controls even with matching feature IDs");
        require(!LogitechModels.HasG502Controls(snapshot with { Features = features.Where(f => f.Id != 0x8110).ToArray() }), "Missing button feature disables the dependent control profile");
        require(!LogitechModels.HasG502Preferences(snapshot with { Features = features.Where(f => f.Id != 0x1300).ToArray() }), "Missing indicator feature disables dependent preferences");
    }
}

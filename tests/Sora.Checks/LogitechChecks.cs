using System.Text.Json;
using Sora.Core;

static class LogitechChecks
{
    public static void Run(Action<bool, string> require)
    {
        var request = LogitechProtocol.Request(0xff, 10, 2, 9, [0]);
        require(Convert.ToHexString(request) == "10FF0A29000000", "HID++ short request framing");
        var reply = Convert.FromHexString("11FF0A2900032003E80000000000000000000000");
        require(LogitechProtocol.Matches(reply, request), "Long response matches short request across collections");
        foreach (var offset in new[] { 1, 2, 3 })
        {
            var wrong = reply.ToArray(); wrong[offset] ^= 1;
            require(!LogitechProtocol.Matches(wrong, request), "Other device/feature/software ID rejected");
        }
        require(!LogitechProtocol.Matches(reply[..4], request), "Truncated reply rejected");
        require(!LogitechProtocol.Matches(Convert.FromHexString("11FF0A2000032003E80000000000000000000000"), request), "Notification cannot complete a request");
        var error = Convert.FromHexString("11FFFF0A29020000000000000000000000000000");
        require(LogitechProtocol.Matches(error, request), "Device errors correlate with original feature and software ID");
        require(LogitechProtocol.IsResponseCandidate(error), "Device error with zero low nibble reaches response correlation");
        require(!LogitechProtocol.IsResponseCandidate(Convert.FromHexString("11FF0A2000032003E80000000000000000000000")), "zero-ID notifications stay out of the response queue");
        Reject(() => LogitechProtocol.CheckReply(error, request), "Device error must not become a successful read");
        Reject(() => LogitechProtocol.Request(0xff, 1, 16, 1, []), "Invalid function");
        Reject(() => LogitechProtocol.Request(0xff, 1, 1, 0, []), "Event software ID");
        Reject(() => LogitechProtocol.Request(0xff, 1, 1, 1, new byte[17]), "Oversized payload");

        // G502 HERO response bytes from independent live queries, 17 October 2026.
        var sensor = LogitechProtocol.ParseSensor(0, Convert.FromHexString("000064E0326400000000000000000000"), Convert.FromHexString("00032003E80000000000000000000000"));
        require(sensor.Dpi == 800 && sensor.DefaultDpi == 1000 && sensor.Minimum == 100 && sensor.Maximum == 25600 && sensor.Step == 50, "Captured G502 DPI range");
        require(sensor.Accepts(100) && sensor.Accepts(25600) && !sensor.Accepts(99) && !sensor.Accepts(801) && !sensor.Accepts(25650), "DPI range and step enforcement");
        require(LogitechProtocol.ParseReportRates(0x8b).SequenceEqual(new[] { 1000, 500, 250, 125 }), "Report-period bitmap decoded in Hz");
        Reject(() => LogitechProtocol.ParseSensor(0, [0], [0, 0, 0, 0, 0]), "Malformed sensor list");
        Reject(() => LogitechProtocol.ParseSensor(1, [0, 1, 0], [0, 0, 0, 0, 0]), "Wrong sensor index");

        // G HUB's captured 1300 fn5 is big-endian, while its fn4 getter reports byte fields.
        var indicatorCommand = LogitechProtocol.Request(0xff, 5, 5, 11, LogitechDpiIndicator.Encode(0, 2, 2));
        require(Convert.ToHexString(indicatorCommand) == "11FF055B00000200020000000000000000000000", "DPI indicator command matches the captured G HUB stage-2 command");
        require(LogitechDpiIndicator.Decode(0, Convert.FromHexString("00010000000000000000000000000000")) == (1, 0), "Native indicator getter decodes OFF without replaying it as a setter");
        require(LogitechDpiIndicator.Decode(0, [0, 2, 0, 5, 0, 0]) == (2, 5), "DPI display status carries mode and stage in byte fields");
        require(LogitechDpiIndicator.Decode(0, Convert.FromHexString("00020000000000000000000000000000")) == (2, 0), "Firmware can report ON without echoing the requested display pattern");
        Reject(() => LogitechDpiIndicator.Decode(0, [0, 0, 2, 0, 2]), "Setter data is not a valid getter response");
        require(Convert.ToHexString(LogitechProtocol.Request(0xff, 10, 3, 11, [0, 3, 32])) == "10FF0A3B000320", "800 DPI writes actual 0x0320 sensor counts, with no factor of two");
        using (var capture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "logitech-g502-dpi-indicator.json"))))
        {
            foreach (var change in capture.RootElement.GetProperty("changes").EnumerateArray())
            {
                var dpi = change.GetProperty("dpi").GetInt32(); var level = change.GetProperty("level").GetInt32();
                require(Convert.ToHexString(LogitechProtocol.Request(0xff, 10, 3, 12, [0, (byte)(dpi >> 8), (byte)dpi])) == change.GetProperty("sensor").GetString(), "Sensor request matches physical G HUB button capture at " + dpi);
                require(Convert.ToHexString(LogitechProtocol.Request(0xff, 5, 5, 12, LogitechDpiIndicator.Encode(0, 2, level))) == change.GetProperty("indicator").GetString(), "Indicator request matches physical G HUB button capture at " + dpi);
                require(LogitechDpiIndicator.Level(LogitechControlProfile.Create(), dpi) == level, "Stage and Shift display slots match captured G HUB ordering");
            }
            require(Convert.ToHexString(LogitechProtocol.Request(0xff, 5, 5, 12, LogitechDpiIndicator.Encode(0, 1, 0))) == capture.RootElement.GetProperty("off").GetString(), "Automatic indicator hide matches the captured G HUB Off command");
        }

        require(LogitechLighting.Encode(1, "#FF8040", 5000, 50).SequenceEqual(new byte[] { 1, 127, 64, 32, 0, 0, 0, 0, 0, 0, 0 }), "Fixed lighting brightness scales RGB");
        var breath = LogitechLighting.Encode(10, "#53D0EF", 5000, 75);
        require(Convert.ToHexString(breath) == "0A53D0EF1388004B000000", "Breathing color, period, firmware-default waveform and intensity layout");
        var wireBreath = LogitechLighting.ToWire(breath, [0, 1, 10, 3]);
        require(wireBreath[0] == 2 && breath[0] == 10, "Live 8070 effect index differs from onboard effect ID");
        require(LogitechLighting.FromWire(wireBreath, [0, 1, 10, 3]).SequenceEqual(breath), "Effect index maps back to semantic ID at the protocol boundary");
        Reject(() => LogitechLighting.FromWire(new byte[] { 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, [0, 1, 10, 3]), "Unknown effect index rejected");
        var cycle = LogitechLighting.Encode(3, "FFFFFF", 8000, 100);
        require(cycle[6] == 0x1f && cycle[7] == 0x40 && cycle[8] == 0, "Cycle uses its distinct period fields and zero for 100 percent");
        Reject(() => LogitechLighting.Encode(1, "#ff", 5000, 100), "Short hex rejected");
        Reject(() => LogitechLighting.Encode(4, "ffffff", 5000, 100), "Undiscovered effects rejected");
        Reject(() => LogitechLighting.Encode(3, "ffffff", 65536, 100), "Period overflow rejected");

        require(LogitechProtocol.DescribeBinding(Convert.FromHexString("80010001")) == "Left click", "Mouse button bitmap is big endian");
        require(LogitechProtocol.DescribeBinding(Convert.FromHexString("900BFFFF")) == "G-Shift", "G-Shift binding decoded");
        require(LogitechProtocol.DescribeBinding(Convert.FromHexString("9007FFFF")) == "DPI shift", "DPI shift binding decoded");
        require(LogitechProtocol.DescribeBinding(Convert.FromHexString("FFFFFFFF")) == "Disabled", "Disabled binding decoded");
        var slots = JsonSerializer.Deserialize<LogitechOnboardSlot[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "logitech-g502-onboard.json")))!;
        require(slots.Length == 5, "Five captured onboard profile sectors");
        foreach (var slot in slots)
        {
            require(LogitechProtocol.ValidChecksum(slot.Raw), "Captured sector CRC verified");
            var damaged = slot.Raw.ToArray(); damaged[32] ^= 1;
            require(!LogitechProtocol.ValidChecksum(damaged), "Damaged onboard data fails CRC");
        }

        var state = new LogitechSnapshot("G502 HERO", "fixture", 0xc08b, "4.2", [], [sensor], 1000, [1000, 500, 250, 125], 2, []);
        var preset = new LogitechPreset(1, "Precision", 0xc08b, 1600, 500);
        LogitechProfiles.Validate(preset, state);
        Reject(() => LogitechProfiles.Validate(preset with { Dpi = 801 }, state), "Import invalid DPI");
        Reject(() => LogitechProfiles.Validate(preset with { ProductId = 0xc539 }, state), "Import wrong model");
        Reject(() => LogitechProfiles.Validate(preset with { Version = 2 }, state), "Import unknown version");
        Reject(() => LogitechProfiles.Validate(preset with { Name = "" }, state), "Empty preset name");
        Reject(() => LogitechProfiles.Validate(preset with { ReportRate = 8000 }, state), "Unsupported report rate");
        var directory = Path.Combine(AppContext.BaseDirectory, "sora-logitech-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = Path.Combine(directory, "preset.json"); LogitechProfiles.Save(file, preset);
            require(LogitechProfiles.Load(file, state) == preset, "Atomic preset export/import round trip");
        }
        finally
        {
            File.Delete(Path.Combine(directory, "preset.json")); File.Delete(Path.Combine(directory, "preset.json.tmp"));
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }

        static void Reject(Action action, string reason)
        {
            try { action(); }
            catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException) { return; }
            throw new Exception("Expected rejection: " + reason);
        }
    }
}

using Sora.Core;

static class LenovoLegionProtocolChecks
{
    public static void Run(Action<bool, string> require)
    {
        // Source-reference vectors, not USB captures or evidence of a qualified physical device.
        byte[] colors = [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255];
        var staticReport = LenovoLegionProtocol.FourZoneReport(LegionFourZoneEffect.Static, 1, 2, colors);
        require(Convert.ToHexString(staticReport) == "CC16010102FF000000FF000000FFFFFFFF00000000000000000000000000000000",
            "Four-zone reference vector: report header, four RGB triples, direction and zero padding");
        require(LenovoLegionProtocol.FourZoneOffReport().SequenceEqual(new byte[] { 0xCC, 0x16 }.Concat(new byte[31])),
            "Off uses an explicit firmware off report, not a black animated effect");
        var left = LenovoLegionProtocol.FourZoneReport(LegionFourZoneEffect.WaveLeft, 4, 1, colors);
        var right = LenovoLegionProtocol.FourZoneReport(LegionFourZoneEffect.WaveRight, 4, 1, colors);
        require(left.Length == 33 && left[2] == 4 && left[18] == 0 && left[19] == 1 && left.AsSpan(5, 12).ToArray().All(value => value == 0),
            "Wave left shares effect 4, selects byte 19 and omits static zone colors");
        require(right[2] == 4 && right[18] == 1 && right[19] == 0, "Wave right changes the direction byte, not the wire effect ID");
        var breathing = LenovoLegionProtocol.FourZoneReport(LegionFourZoneEffect.Breathing, 2, 1, colors);
        require(breathing[2] == 3 && breathing.AsSpan(5, 12).SequenceEqual(colors), "Breathing carries the four zone colors");
        var cycle = LenovoLegionProtocol.FourZoneReport(LegionFourZoneEffect.ColorCycle, 3, 2, colors);
        require(cycle[2] == 6 && cycle.AsSpan(5).ToArray().All(value => value == 0), "Color cycle does not leak stale colors or direction flags");
        Reject(() => LenovoLegionProtocol.FourZoneReport((LegionFourZoneEffect)255, 1, 1, colors), require);
        Reject(() => LenovoLegionProtocol.FourZoneReport(LegionFourZoneEffect.Static, 0, 1, colors), require);
        Reject(() => LenovoLegionProtocol.FourZoneReport(LegionFourZoneEffect.Static, 5, 1, colors), require);
        Reject(() => LenovoLegionProtocol.FourZoneReport(LegionFourZoneEffect.Static, 1, 0, colors), require);
        Reject(() => LenovoLegionProtocol.FourZoneReport(LegionFourZoneEffect.Static, 1, 3, colors), require);
        Reject(() => LenovoLegionProtocol.FourZoneReport(LegionFourZoneEffect.Static, 1, 1, new byte[11]), require);

        require(LenovoLegionProtocol.EncodePowerMode(LegionPowerMode.Extreme) == 224 &&
            LenovoLegionProtocol.EncodePowerMode(LegionPowerMode.Custom) == 255 &&
            LenovoLegionProtocol.DecodePowerMode(1) == LegionPowerMode.Quiet, "Power codec uses firmware values, not Toolkit's offset UI enum");
        foreach (uint unknown in new uint[] { 0, 4, 223, 254, uint.MaxValue })
        {
            var rejected = false;
            try { LenovoLegionProtocol.DecodePowerMode(unknown); } catch (InvalidDataException) { rejected = true; }
            require(rejected, "Unknown power value remains unknown: " + unknown);
        }
        require(LenovoLegionProtocol.DecodeChargeMode(0) == LegionChargeMode.Normal &&
            LenovoLegionProtocol.DecodeChargeMode(4) == LegionChargeMode.RapidCharge &&
            LenovoLegionProtocol.DecodeChargeMode(0x24) == LegionChargeMode.Conservation,
            "Charge-mode flag precedence matches the driver protocol");
        require(LenovoLegionProtocol.ChargeModeRequests(LegionChargeMode.Conservation).SequenceEqual(new uint[] { 8, 3 }) &&
            LenovoLegionProtocol.ChargeModeRequests(LegionChargeMode.Normal).SequenceEqual(new uint[] { 5, 8 }) &&
            LenovoLegionProtocol.ChargeModeRequests(LegionChargeMode.RapidCharge).SequenceEqual(new uint[] { 5, 7 }),
            "Charge-mode transitions preserve the two ordered driver requests");
        Reject(() => LenovoLegionProtocol.ChargeModeRequests((LegionChargeMode)99), require);

        var table = LenovoLegionProtocol.FanTableReport([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        require(table.Length == 64 && Convert.ToHexString(table.AsSpan(0, 26)) ==
            "01000A0000000100020003000400050006000700080009000A00" && table.AsSpan(26).ToArray().All(value => value == 0),
            "Fan table uses a 32-bit count, ten little-endian 16-bit levels and 38 zero padding bytes");
        var endian = LenovoLegionProtocol.FanTableReport([0x1234, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        require(endian[6] == 0x34 && endian[7] == 0x12, "Fan table does not truncate or endian-swap raw 16-bit levels");
        Reject(() => LenovoLegionProtocol.FanTableReport(new ushort[9]), require);
        Reject(() => LenovoLegionProtocol.FanTableReport(new ushort[11]), require);

        foreach (var operation in new byte[] { 0xd1, 0xcd, 0xca })
        {
            var query = LenovoLegionSpectrum.Query(operation);
            require(query.Length == 960 && query[0] == 7 && query[1] == operation && query[2] == 0xc0 && query[3] == 3 && query.AsSpan(4).ToArray().All(value => value == 0),
                "Spectrum queries use their own 960-byte protocol");
            require(LenovoLegionSpectrum.ReadResponse(operation, query) == 0, "Spectrum response decoder accepts the requested operation");
            var failed = false;
            try { LenovoLegionSpectrum.ReadResponse(operation, query.AsSpan(0, 32)); } catch (InvalidDataException) { failed = true; }
            require(failed, "Four-zone-sized and truncated data cannot be decoded as Spectrum");
        }
        var wrong = LenovoLegionSpectrum.Query(0xca); wrong[1] = 0xcd;
        var unrelated = false;
        try { LenovoLegionSpectrum.ReadResponse(0xca, wrong); } catch (InvalidDataException) { unrelated = true; }
        require(unrelated, "Unrelated Spectrum response cannot supply a profile value");
        require(Convert.ToHexString(LenovoLegionSpectrum.Brightness(9).AsSpan(0, 5)) == "07CEC00309" &&
            Convert.ToHexString(LenovoLegionSpectrum.Profile(6).AsSpan(0, 5)) == "07C8C00306", "Spectrum brightness and profile reference prefixes match upstream structs");
        Reject(() => LenovoLegionSpectrum.Brightness(10), require);
        Reject(() => LenovoLegionSpectrum.Profile(7), require);
    }

    private static void Reject(Action action, Action<bool, string> require)
    {
        var rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; }
        require(rejected, "Malformed Legion protocol input rejected before serialization");
    }
}

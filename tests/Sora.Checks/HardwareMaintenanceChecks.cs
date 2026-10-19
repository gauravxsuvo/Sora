using Sora.Core;

internal static class HardwareMaintenanceChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var notice = HardwareMaintenance.StorageNotice("KIOXIA-EXCERIA G3 SSD", "EVFATR.0");
        check(notice != null && notice.Url == HardwareMaintenance.KioxiaAdvisory && notice.Detail.Contains("EVFATR.1") && notice.Detail.Contains("20 April 2026"), "Known installed EXCERIA G3 firmware matches a dated official advisory");
        check(HardwareMaintenance.StorageNotice(" kioxia-exceria g3 ssd ", " evfatr.0 ") != null, "Identity matching tolerates only case and surrounding whitespace");
        foreach (var model in new string?[] { null, "", "Unknown", "KIOXIA-EXCERIA PLUS G3 SSD", "KIOXIA-EXCERIA G2 SSD", "KIOXIA-EXCERIA G3 SSD OTHER", "ZEB-MN52" })
            check(HardwareMaintenance.StorageNotice(model, "EVFATR.0") == null, "Another or unknown model never inherits EXCERIA G3 firmware advice");
        foreach (var firmware in new string?[] { null, "", "Unknown", "EVFATR.1", "EVFATR.2", "EVFATR.0X", "EVFATR" })
            check(HardwareMaintenance.StorageNotice("KIOXIA-EXCERIA G3 SSD", firmware) == null, "Unknown and other firmware versions are not guessed");
        check(HardwareMaintenance.Routes.Count == 8 && HardwareMaintenance.Routes.Select(r => r.Url).Distinct().Count() == 8, "Eight distinct maintenance entry points");
        foreach (var route in HardwareMaintenance.Routes)
            check(Uri.TryCreate(route.Url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && HardwareMaintenance.IsSupportUrl(route.Url) && route.Coverage.Length > 0, "Reviewed entry points use HTTPS and describe coverage");
        foreach (var url in new string?[] { null, "", "file:///C:/Windows/System32/cmd.exe", "https://evil.example/", HardwareMaintenance.KioxiaUtility + "/evil", "https://www.nvidia.com.evil.example/en-us/drivers/" })
            check(!HardwareMaintenance.IsSupportUrl(url), "Only exact reviewed support URLs are accepted");
        check(HardwareMaintenance.Coverage.Contains("not checked automatically") && HardwareMaintenance.Coverage.Contains("do not mean an update is available"), "Manual routes cannot imply automatic firmware coverage");
    }
}

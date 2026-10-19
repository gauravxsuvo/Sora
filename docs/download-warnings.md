# Sora download warnings

The reported Chrome warning was "suspicious download blocked". That is a browser security verdict, not evidence that the installer has been cleared or merely lacks a familiar icon.

Sora installers currently have no Windows Authenticode publisher signature. Sora's pinned P-256 release signatures and SHA-256 checks protect its own update channel; they do not establish Google Safe Browsing or Windows SmartScreen reputation. Changing compression, renaming the installer or bypassing browser checks is not a fix for a security verdict.

Google describes malicious, suspicious, unverified and insecure downloads in its [Chrome download guidance](https://support.google.com/chrome/answer/6261569). The exact warning and More details should be retained when reporting a problem. If Chrome offers to submit the file for further checks, the user can use that built-in action. Do not disable Safe Browsing or Windows Security to get around the warning.

For each release, keep a stable HTTPS URL, versioned artifacts, signed release metadata, final file hashes, dependency notices and build audit. Compare local and hosted installer bytes. Where available, scan the exact final installer with Windows Security. A clean scan is useful evidence but cannot guarantee Chrome acceptance. If a site warning is reported in Google Search Console's Security Issues report, follow its review process after fixing the issue. A file download warning alone does not prove a site-wide Search Console issue.

The free-only policy remains in effect. No signing certificate or paid service has been purchased. Free release signatures cannot promise removal of browser warnings.

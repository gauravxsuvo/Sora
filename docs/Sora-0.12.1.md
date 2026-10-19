# Sora 0.12.1 — reference-derived circular logo

Local candidate; not installed or promoted by this branding change.

- Replaces the cloud mark with the supplied circular flowing-cut silhouette, reconstructed as editable SVG paths with transparent gaps.
- Adds a violet/lilac gradient, solid white/ink variants, a currentColor SVG, transparent PNG exports up to 1024 px and a multi-size Windows ICO.
- Updates application sidebar, executable/window/dialog/taskbar/tray icon sources, setup executable icon, website header/download/footer, favicon, touch icon, social image and README mark.
- Generates all active logo variants and WPF geometry from one master. The compatibility export command uses the new mark.

The updater behavior is unchanged. Version 0.12.1 keeps this installer distinct from the previously signed 0.12.0 candidate; previously built artifacts are retained. Public hosting and installed-machine update qualification remain as described in the updater operating guide.

Local validation: application/helper publish and installer packaging succeeded; the maintenance UI checks passed; all six website tests passed with the new asset routes. Headless Edge desktop/mobile checks at 1440/390 px confirmed no horizontal overflow, and the fresh app capture shows the replacement sidebar mark. Asset checks verified transparent 1024 px PNG output, matching master/website SVG exports and nine ICO entries. Evidence and candidate installer are under `artifacts/brand-refresh/`. No installer was run, update feed promoted or hosting deployment performed.

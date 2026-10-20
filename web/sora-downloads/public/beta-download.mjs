export async function loadBeta({ document: page = document, fetchImpl = fetch } = {}) {
  const button = page.querySelector('#beta-download-button');
  const status = page.querySelector('#beta-status');
  const notes = page.querySelector('#beta-release-link');
  const retry = page.querySelector('#beta-retry');
  button.removeAttribute('href'); button.setAttribute('aria-disabled', 'true'); button.setAttribute('tabindex', '-1');
  page.querySelector('#beta-download-label').textContent = 'Download beta';
  notes.href = 'https://github.com/gauravxsuvo/Sora/releases'; notes.textContent = 'View releases on GitHub ↗';
  status.textContent = 'Checking beta availability…'; retry.hidden = true;
  try {
    const response = await fetchImpl('/api/beta-release', { cache: 'no-store', signal: AbortSignal.timeout(10000), redirect: 'error' });
    if (!response.ok) throw new Error('Unavailable');
    const beta = await response.json();
    if (beta.available !== true) { status.textContent = 'No beta download has been published yet.'; return; }
    const version = beta.version;
    if (typeof version !== 'string' || !/^\d{1,5}\.\d{1,5}\.\d{1,5}-(?:beta|alpha|rc)\.\d{1,5}$/.test(version) ||
        !['installer', 'portable'].includes(beta.kind) || !Number.isSafeInteger(beta.size) || beta.size < 1 || beta.size > 2 * 1024 ** 3) throw new Error('Invalid beta');
    const assetName = beta.kind === 'installer' ? `Sora-Setup-${version}-x64.exe` : `Sora-${version}-win-x64.zip`;
    if (![version, `v${version}`].some(tag => beta.downloadUrl === `https://github.com/gauravxsuvo/Sora/releases/download/${tag}/${assetName}`
        && beta.releaseUrl === `https://github.com/gauravxsuvo/Sora/releases/tag/${tag}`)) throw new Error('Invalid beta link');
    button.href = beta.downloadUrl; button.removeAttribute('aria-disabled'); button.removeAttribute('tabindex');
    page.querySelector('#beta-download-label').textContent = `Download beta · ${version}`;
    notes.href = beta.releaseUrl; notes.textContent = 'Read beta release notes ↗';
    status.textContent = `Experimental · ${(beta.size / 1048576).toFixed(1)} MB · ${beta.kind === 'portable' ? 'Portable ZIP' : 'Windows installer'}. New features may not be fully tested.`;
  } catch {
    status.textContent = 'Beta availability could not be checked. Try again or view releases on GitHub.';
    retry.hidden = false;
  }
}

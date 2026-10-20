import { verifyRelease } from './release-verification.mjs';
import { loadBeta } from './beta-download.mjs';

const status = document.querySelector('#release-status');
const button = document.querySelector('#download-button');
const retry = document.querySelector('#retry');
async function boundedJson(response) {
  const reader = response.body.getReader(); const chunks = []; let size = 0;
  try {
    while (true) {
      const { done, value } = await reader.read(); if (done) break;
      size += value.length; if (size > 131072) throw new Error('Invalid release metadata size.');
      chunks.push(value);
    }
  } finally { await reader.cancel(); }
  const result = new Uint8Array(size); let offset = 0;
  for (const chunk of chunks) { result.set(chunk, offset); offset += chunk.length; }
  return JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(result));
}
async function loadRelease() {
  retry.hidden = true; button.removeAttribute('href'); button.setAttribute('aria-disabled', 'true');
  status.textContent = 'Checking the latest release…';
  try {
    const options = { cache: 'no-store', redirect: 'error', signal: AbortSignal.timeout(15000) };
    const [policyResponse, feedResponse] = await Promise.all([fetch('/update-policy.json', options), fetch('/channels/stable.json', options)]);
    if (!policyResponse.ok) throw new Error('Download verification is temporarily unavailable. Please try again shortly.');
    if (feedResponse.status === 404) throw new Error('A new release is being prepared. Check again shortly or use GitHub Releases.');
    if (!feedResponse.ok) throw new Error('The download service is temporarily unavailable. Please try again shortly.');
    if (Number(feedResponse.headers.get('content-length')) > 131072) throw new Error('Invalid release metadata.');
    const release = await verifyRelease(await boundedJson(feedResponse), await boundedJson(policyResponse));
    button.href = release.installerUrl; button.removeAttribute('aria-disabled');
    document.querySelector('#version').textContent = `Sora ${release.version}`;
    status.textContent = `${(release.size / 1048576).toFixed(1)} MB · ${new Date(release.publishedAt).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' })} · Stable release`;
    document.querySelector('#notes').textContent = release.notes;
    document.querySelector('#hash').textContent = release.sha256;
    document.querySelector('#release-details').hidden = false;
  } catch (error) {
    const networkError = error instanceof TypeError || error.name === 'TimeoutError' || error.name === 'AbortError';
    status.textContent = networkError ? 'We could not reach the download service. Check your connection and try again.' :
      error.message.includes('temporarily') || error.message.includes('being prepared') ? error.message :
      'This release could not be verified. Please try again later or visit the official GitHub Releases page.';
    retry.hidden = false;
    document.querySelector('#release-details').hidden = true;
  }
}
retry.addEventListener('click', loadRelease);
button.addEventListener('click', event => { if (button.getAttribute('aria-disabled') === 'true') event.preventDefault(); });
document.querySelector('#copy-hash').addEventListener('click', async () => {
  const message = document.querySelector('#copy-status');
  try { await navigator.clipboard.writeText(document.querySelector('#hash').textContent); message.textContent = 'Checksum copied.'; }
  catch { message.textContent = 'Select the checksum above and copy it manually.'; }
});
loadRelease();
const betaButton = document.querySelector('#beta-download-button');
betaButton.addEventListener('click', event => { if (betaButton.getAttribute('aria-disabled') === 'true') event.preventDefault(); });
document.querySelector('#beta-retry').addEventListener('click', () => loadBeta());
loadBeta();

import { verifyRelease } from './release-verification.mjs';

const status = document.querySelector('#release-status');
const button = document.querySelector('#download');
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
    if (!policyResponse.ok || !feedResponse.ok) throw new Error('The release service is not available yet.');
    if (Number(feedResponse.headers.get('content-length')) > 131072) throw new Error('Invalid release metadata.');
    const release = await verifyRelease(await boundedJson(feedResponse), await boundedJson(policyResponse));
    button.href = release.installerUrl; button.removeAttribute('aria-disabled');
    document.querySelector('#version').textContent = `Sora ${release.version}`;
    status.textContent = `${(release.size / 1048576).toFixed(1)} MB · ${new Date(release.publishedAt).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' })} · Stable release`;
    document.querySelector('#notes').textContent = release.notes;
    document.querySelector('#hash').textContent = release.sha256;
    document.querySelector('#release-details').hidden = false;
  } catch {
    status.textContent = 'The new download service is not ready yet. You can find existing installers on GitHub Releases.';
    retry.hidden = false;
  }
}
retry.addEventListener('click', loadRelease);
button.addEventListener('click', event => { if (button.getAttribute('aria-disabled') === 'true') event.preventDefault(); });
loadRelease();

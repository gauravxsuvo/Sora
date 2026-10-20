const repository = 'gauravxsuvo/Sora';
const github = `https://github.com/${repository}`;
const api = `https://api.github.com/repos/${repository}/releases?per_page=20`;

// GitHub prereleases are a separate, explicitly experimental download path.
// They never become inputs to Sora's signed stable update feed.
export function selectBeta(releases, now = Date.now()) {
  if (!Array.isArray(releases)) throw new Error('Invalid GitHub release response.');
  const candidates = [];
  for (const release of releases) {
    if (release?.draft !== false || release.prerelease !== true) continue;
    const match = /^v?(\d{1,5}\.\d{1,5}\.\d{1,5}-(?:beta|alpha|rc)\.\d{1,5})$/.exec(release.tag_name ?? '');
    const published = Date.parse(release.published_at);
    if (!match || !Number.isFinite(published) || published > now + 300000 || !Array.isArray(release.assets)) continue;
    const version = match[1];
    for (const [name, kind] of [[`Sora-Setup-${version}-x64.exe`, 'installer'], [`Sora-${version}-win-x64.zip`, 'portable']]) {
      const asset = release.assets.find(item => item?.name === name && item.state === 'uploaded');
      if (!asset || !Number.isSafeInteger(asset.size) || asset.size < 1 || asset.size > 2 * 1024 ** 3) continue;
      const expectedUrl = `${github}/releases/download/${release.tag_name}/${name}`;
      if (asset.browser_download_url !== expectedUrl) continue;
      candidates.push({ available: true, version, kind, size: asset.size, downloadUrl: expectedUrl,
        releaseUrl: `${github}/releases/tag/${release.tag_name}`, publishedAt: new Date(published).toISOString() });
      break; // Prefer the installer when a release includes both formats.
    }
  }
  candidates.sort((a, b) => Date.parse(b.publishedAt) - Date.parse(a.publishedAt));
  return candidates[0] ?? { available: false };
}

async function boundedJson(response) {
  const limit = 1024 * 1024;
  if (!response.body || Number(response.headers.get('content-length')) > limit) throw new Error('Invalid release response size.');
  const reader = response.body.getReader(); const chunks = []; let size = 0;
  try {
    while (true) {
      const { done, value } = await reader.read(); if (done) break;
      size += value.length; if (size > limit) throw new Error('Invalid release response size.');
      chunks.push(value);
    }
  } finally { await reader.cancel(); }
  const bytes = new Uint8Array(size); let offset = 0;
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
  return JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(bytes));
}

export function createBetaLookup({ fetchImpl = fetch, now = Date.now } = {}) {
  let cached, expires = 0, pending;
  return async () => {
    if (cached && now() < expires) return cached;
    if (pending) return pending;
    pending = (async () => {
      try {
        const response = await fetchImpl(api, { redirect: 'error', signal: AbortSignal.timeout(8000),
          headers: { Accept: 'application/vnd.github+json', 'User-Agent': 'Sora-downloads', 'X-GitHub-Api-Version': '2022-11-28' } });
        if (!response.ok) throw new Error('Beta availability could not be checked.');
        cached = selectBeta(await boundedJson(response), now());
        expires = now() + 5 * 60 * 1000;
        return cached;
      } catch {
        // Fail closed: never keep advertising an old asset after a failed refresh.
        cached = { available: false, unavailable: true };
        expires = now() + 60000;
        return cached;
      } finally { pending = undefined; }
    })();
    return pending;
  };
}

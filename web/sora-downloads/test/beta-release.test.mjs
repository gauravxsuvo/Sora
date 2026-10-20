import { test } from 'node:test';
import assert from 'node:assert/strict';
import { selectBeta, createBetaLookup } from '../beta-release.mjs';
import { loadBeta } from '../public/beta-download.mjs';

const github = 'https://github.com/gauravxsuvo/Sora';
function release(version = '0.13.0-beta.2', published = '2026-10-19T00:00:00Z') {
  const tag = `v${version}`;
  return { draft: false, prerelease: true, tag_name: tag, published_at: published,
    assets: [`Sora-${version}-win-x64.zip`, `Sora-Setup-${version}-x64.exe`].map(name => ({
      name, state: 'uploaded', size: 6459111, browser_download_url: `${github}/releases/download/${tag}/${name}`
    })) };
}
const now = Date.parse('2026-10-20T00:00:00Z');

test('selects newest published prerelease with a Windows asset and prefers installer', () => {
  const older = release('0.13.0-beta.1', '2026-10-18T00:00:00Z');
  const latest = release();
  assert.equal(selectBeta([older, latest], now).kind, 'installer');
  latest.assets.pop();
  const beta = selectBeta([latest, older], now);
  assert.equal(beta.kind, 'portable');
  assert.equal(beta.version, '0.13.0-beta.2');
  assert.equal(beta.releaseUrl, `${github}/releases/tag/v0.13.0-beta.2`);
});

test('rejects drafts, stable releases, missing assets and untrusted download links', () => {
  for (const change of [
    { draft: true }, { prerelease: false }, { tag_name: 'v0.13.0' }, { assets: [] },
    { published_at: 'invalid' }, { published_at: '2026-11-01T00:00:00Z' },
    { assets: release().assets.map(asset => ({ ...asset, browser_download_url: 'https://example.com/file.exe' })) },
    { assets: release().assets.map(asset => ({ ...asset, state: 'new' })) },
    { assets: release().assets.map(asset => ({ ...asset, size: 0 })) }
  ]) assert.deepEqual(selectBeta([{ ...release(), ...change }], now), { available: false });
});

test('retired Legion releases cannot replace a supported beta download', () => {
  const retired = release('0.13.0-legion.1', '2026-10-19T12:00:00Z');
  const supported = release('0.13.0-beta.2', '2026-10-18T00:00:00Z');
  assert.equal(selectBeta([retired, supported], now).version, supported.tag_name.slice(1));
  assert.deepEqual(selectBeta([retired], now), { available: false });
});

test('lookup shares concurrent requests, caches results, and clears links on refresh failure', async () => {
  let time = now, requests = 0, fail = false;
  const lookup = createBetaLookup({ now: () => time, fetchImpl: async (url, options) => {
    requests++; assert.equal(new URL(url).hostname, 'api.github.com'); assert.equal(options.redirect, 'error');
    return fail ? new Response('rate limited', { status: 403 }) : Response.json([release()]);
  } });
  const results = await Promise.all([lookup(), lookup(), lookup()]);
  assert.equal(requests, 1); assert.ok(results.every(item => item.available));
  await lookup(); assert.equal(requests, 1);
  time += 300001; fail = true;
  assert.deepEqual(await lookup(), { available: false, unavailable: true });
  await lookup(); assert.equal(requests, 2);
  time += 60001; fail = false;
  assert.equal((await lookup()).available, true); assert.equal(requests, 3);
});

test('lookup handles oversized, malformed and offline responses without advertising downloads', async () => {
  for (const fetchImpl of [
    async () => new Response('x'.repeat(1024 * 1024 + 1)),
    async () => new Response('{bad json'),
    async () => { throw new TypeError('offline'); }
  ]) assert.deepEqual(await createBetaLookup({ fetchImpl })(), { available: false, unavailable: true });
});

function pageFixture() {
  const elements = new Map();
  for (const id of ['beta-download-button', 'beta-status', 'beta-release-link', 'beta-retry', 'beta-download-label']) {
    const attrs = new Map();
    elements.set(`#${id}`, { textContent: '', hidden: false,
      get href() { return attrs.get('href'); }, set href(value) { attrs.set('href', value); },
      setAttribute(key, value) { attrs.set(key, value); }, removeAttribute(key) { attrs.delete(key); },
      getAttribute(key) { return attrs.get(key) ?? null; } });
  }
  return { querySelector: id => { assert.ok(elements.has(id), `Only beta controls should change: ${id}`); return elements.get(id); } };
}

test('beta UI enables validated downloads, and clears previous links when unavailable', async () => {
  const document = pageFixture();
  const button = document.querySelector('#beta-download-button');
  const beta = selectBeta([release()], now);
  await loadBeta({ document, fetchImpl: async () => Response.json(beta) });
  assert.equal(button.href, beta.downloadUrl); assert.equal(button.getAttribute('aria-disabled'), null);
  assert.match(document.querySelector('#beta-status').textContent, /Experimental/);
  await loadBeta({ document, fetchImpl: async () => Response.json({ available: false }) });
  assert.equal(button.href, undefined); assert.equal(button.getAttribute('aria-disabled'), 'true');
  assert.equal(button.getAttribute('tabindex'), '-1');
  assert.match(document.querySelector('#beta-status').textContent, /No beta download/);
  assert.equal(document.querySelector('#beta-retry').hidden, true);
});

test('beta UI offers retry and stays disabled for failures and unsafe links', async () => {
  for (const fetchImpl of [
    async () => new Response('', { status: 503 }),
    async () => { throw new TypeError('offline'); },
    async () => Response.json({ ...selectBeta([release()], now), downloadUrl: 'https://example.com/malware.exe' })
  ]) {
    const document = pageFixture(); await loadBeta({ document, fetchImpl });
    assert.equal(document.querySelector('#beta-download-button').href, undefined);
    assert.equal(document.querySelector('#beta-retry').hidden, false);
  }
});

test('beta UI rejects a cached retired Legion download even with a valid GitHub URL', async () => {
  const document = pageFixture();
  const retired = release('0.13.0-legion.1');
  const asset = retired.assets[1];
  await loadBeta({ document, fetchImpl: async () => Response.json({
    available: true, version: '0.13.0-legion.1', kind: 'installer', size: asset.size,
    downloadUrl: asset.browser_download_url, releaseUrl: `${github}/releases/tag/${retired.tag_name}`
  }) });
  assert.equal(document.querySelector('#beta-download-button').href, undefined);
  assert.equal(document.querySelector('#beta-download-button').getAttribute('aria-disabled'), 'true');
});

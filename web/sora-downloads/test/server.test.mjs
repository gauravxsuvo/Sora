import { test } from 'node:test';
import assert from 'node:assert/strict';
import { Readable } from 'node:stream';
import { once } from 'node:events';
import { makeServer } from '../server.mjs';
import { storageConfig } from '../storage.mjs';

async function withServer(storage, run, betaLookup = async () => ({ available: false })) {
  const server = makeServer({ storage, betaLookup, logger: { error() {} } }); server.listen(0, '127.0.0.1'); await once(server, 'listening');
  try { await run(`http://127.0.0.1:${server.address().port}`); }
  finally { server.closeAllConnections(); await new Promise(resolve => server.close(resolve)); }
}
test('static website, method restrictions and honest unconfigured feed', () => withServer(null, async base => {
  const home = await fetch(base); assert.equal(home.status, 200); const html = await home.text(); assert.match(html, /One setup/); assert.doesNotMatch(html, /\u2014/);
  assert.match(home.headers.get('content-security-policy'), /object-src 'none'/);
  assert.match(html, /id="beta-download-button"[^>]*aria-disabled="true"/);
  assert.equal((await fetch(base + '/beta-download.mjs')).status, 200);
  for (const path of ['/styles.css', '/app.mjs', '/release-verification.mjs', '/sora-mark.svg', '/sora-mark.png', '/favicon.ico', '/apple-touch-icon.png', '/sora-updates.png', '/update-policy.json']) assert.equal((await fetch(base + path)).status, 200, path);
  assert.equal((await fetch(base + '/healthz')).status, 200);
  assert.equal((await fetch(base + '/channels/stable.json')).status, 503);
  assert.equal((await fetch(base + '/channels/beta.json')).status, 503);
  assert.equal((await fetch(base + '/channels/unknown.json')).status, 404);
  assert.equal((await fetch(base + '/', { method: 'POST' })).status, 405);
  assert.equal((await fetch(base + '/.release-keys/key.pem')).status, 404);
  assert.equal((await fetch(base + '/releases/1.0.0/win-x64/Sora-Setup-2.0.0-x64.exe')).status, 404);
}));

test('beta availability endpoint supports GET and HEAD without storage or cached metadata', () => withServer(null, async base => {
  const response = await fetch(base + '/api/beta-release');
  assert.equal(response.status, 200); assert.equal(response.headers.get('cache-control'), 'no-store');
  assert.deepEqual(await response.json(), { available: false });
  const head = await fetch(base + '/api/beta-release', { method: 'HEAD' });
  assert.equal(head.status, 200); assert.equal(await head.text(), '');
}));

test('beta lookup outages return a retryable unavailable response', () => withServer(null, async base => {
  const response = await fetch(base + '/api/beta-release');
  assert.equal(response.status, 503); assert.equal(response.headers.get('retry-after'), '60');
  assert.equal(response.headers.get('cache-control'), 'no-store');
  assert.deepEqual(await response.json(), { available: false, unavailable: true });
}, async () => ({ available: false, unavailable: true })));
test('release proxy streams ranges and HEAD; feeds are never cached', () => withServer({
  async get(key, options) {
    assert.match(key, /^(channels|releases)\//);
    return { Body: Readable.from(['abc']), ContentLength: 3, ETag: '"test"', ...(options.range ? { ContentRange: 'bytes 0-2/100' } : {}) };
  }
}, async base => {
  const path = '/releases/0.12.0/win-x64/Sora-Setup-0.12.0-x64.exe';
  const file = await fetch(base + path, { headers: { range: 'bytes=0-2' } });
  assert.equal(file.status, 206); assert.equal(await file.text(), 'abc'); assert.equal(file.headers.get('content-range'), 'bytes 0-2/100');
  assert.match(file.headers.get('cache-control'), /immutable/); assert.match(file.headers.get('content-disposition'), /Sora-Setup-0.12.0/);
  const head = await fetch(base + path, { method: 'HEAD' }); assert.equal(head.status, 200); assert.equal(await head.text(), '');
  const feed = await fetch(base + '/channels/stable.json'); assert.equal(feed.headers.get('cache-control'), 'no-store');
  const betaFeed = await fetch(base + '/channels/beta.json'); assert.equal(betaFeed.headers.get('cache-control'), 'no-store');
  const betaPath = '/releases/0.13.0-beta.3/win-x64/Sora-Setup-0.13.0-beta.3-x64.exe';
  const beta = await fetch(base + betaPath); assert.equal(beta.status, 200); assert.equal(await beta.text(), 'abc');
  assert.match(beta.headers.get('content-disposition'), /0.13.0-beta.3/);
  assert.equal((await fetch(base + betaPath.replace('0.13.0-beta.3-x64.exe', '0.13.0-beta.4-x64.exe'))).status, 404);
  assert.equal((await fetch(base + betaPath.replaceAll('-beta.3', '-rc.3'))).status, 404);
  assert.equal((await fetch(base + path, { headers: { range: 'bytes=0-2,5-7' } })).status, 416);
}));
test('storage failures do not leak credentials and rate limits can be retried', () => withServer({
  async get() { throw Object.assign(new Error('secret should not appear'), { name: 'SlowDown', $metadata: { httpStatusCode: 429 } }); }
}, async base => {
  const response = await fetch(base + '/channels/stable.json'); assert.equal(response.status, 429); assert.equal(response.headers.get('retry-after'), '60'); assert.doesNotMatch(await response.text(), /secret/);
}));
test('storage uses internal server endpoint and safe preview prefixes', () => {
  const config = storageConfig({ S3_ENDPOINT: 'https://storage.example', S3_INTERNAL_ENDPOINT: 'http://storage:9000', S3_BUCKET: 'sora', S3_ACCESS_KEY_ID: 'test', S3_SECRET_ACCESS_KEY: 'test', S3_PREFIX: 'preview/pr-1/' });
  assert.equal(config.endpoint, 'http://storage:9000'); assert.equal(config.prefix, 'preview/pr-1/public/');
  assert.equal(storageConfig({}), null);
  assert.throws(() => storageConfig({ S3_ENDPOINT: 'https://storage.example', S3_BUCKET: 'sora', S3_ACCESS_KEY_ID: 'test', S3_SECRET_ACCESS_KEY: 'test', S3_PREFIX: '../' }));
});

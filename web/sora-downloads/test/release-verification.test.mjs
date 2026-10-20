import { test } from 'node:test';
import assert from 'node:assert/strict';
import { generateKeyPairSync, sign } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { verifyRelease } from '../public/release-verification.mjs';

const { privateKey, publicKey } = generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
const policy = { publicKeys: { test: publicKey.export({ type: 'spki', format: 'der' }).toString('base64') }, allowedHosts: ['sora.portways.app'] };
const release = { schemaVersion: 1, product: 'Sora', sequence: 1, channel: 'stable', platform: 'win-x64', version: '0.12.0',
  publishedAt: new Date(Date.now() - 1000).toISOString(), expiresAt: new Date(Date.now() + 86400000).toISOString(),
  size: 100, sha256: 'a'.repeat(64), notes: 'Release notes', installerUrl: 'https://sora.portways.app/releases/0.12.0/win-x64/Sora-Setup-0.12.0-x64.exe' };
function signed(value) {
  const bytes = Buffer.from(JSON.stringify(value));
  return { keyId: 'test', algorithm: 'ECDSA-P256-SHA256-P1363', payload: bytes.toString('base64'), signature: sign('sha256', bytes, { key: privateKey, dsaEncoding: 'ieee-p1363' }).toString('base64') };
}
test('website verifies the same P-256/P1363 release contract as the desktop', async () => {
  assert.deepEqual(await verifyRelease(signed(release), policy), release);
  const envelope = signed(release); envelope.payload = Buffer.from(JSON.stringify({ ...release, size: 101 })).toString('base64');
  await assert.rejects(verifyRelease(envelope, policy), /signature/);
  for (const invalid of [{ expiresAt: '2020-01-01' }, { installerUrl: 'https://evil.example/setup.exe' }, { version: '0.12.0-beta' }, { schemaVersion: 2 }, { platform: 'win-arm64' }, { size: -1 }, { notes: '<script>' + 'x'.repeat(16000) }])
    await assert.rejects(verifyRelease(signed({ ...release, ...invalid }), policy));
});
test('website public trust policy stays in sync with the desktop policy', async () => {
  const website = JSON.parse(await readFile(new URL('../public/update-policy.json', import.meta.url)));
  const desktop = JSON.parse(await readFile(new URL('../../../release/update-policy.json', import.meta.url)));
  assert.deepEqual(website, desktop);
  assert.ok(!('privateKey' in website));
});

test('beta publishing retains signatures, strict versions and channel isolation', async () => {
  const betaPolicy = { ...policy, channel: 'beta' };
  const beta = { ...release, channel: 'beta', version: '0.13.0-beta.3', installerUrl: 'https://sora.portways.app/releases/0.13.0-beta.3/win-x64/Sora-Setup-0.13.0-beta.3-x64.exe' };
  assert.deepEqual(await verifyRelease(signed(beta), betaPolicy), beta);
  await assert.rejects(verifyRelease(signed(beta), policy));
  await assert.rejects(verifyRelease(signed(release), betaPolicy));
  for (const version of ['0.13.0', '0.13.0-beta.0', '0.13.0-beta.01', '0.13.0-beta.65536', '0.013.0-beta.1', '0.13.0-beta.2+hash'])
    await assert.rejects(verifyRelease(signed({ ...beta, version }), betaPolicy));
  const bad = signed(beta); bad.signature = signed(release).signature;
  await assert.rejects(verifyRelease(bad, betaPolicy), /signature/);
});

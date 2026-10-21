const decode = value => Uint8Array.from(atob(value), character => character.charCodeAt(0));
const numericVersion = value => /^\d{1,5}\.\d{1,5}\.\d{1,5}$/.test(value) && value.split('.').every(part => Number(part) <= 65535 && (part === '0' || !part.startsWith('0')));

export async function verifyRelease(envelope, policy, now = Date.now()) {
  const channel = policy.channel ?? 'stable';
  if (channel !== 'stable') throw new Error('Only stable releases are published.');
  if (envelope.algorithm !== 'ECDSA-P256-SHA256-P1363' || !Object.hasOwn(policy.publicKeys, envelope.keyId)) throw new Error('Untrusted release signing key.');
  if (typeof envelope.payload !== 'string' || envelope.payload.length > 128 * 1024) throw new Error('Invalid release metadata size.');
  const payload = decode(envelope.payload);
  const key = await crypto.subtle.importKey('spki', decode(policy.publicKeys[envelope.keyId]), { name: 'ECDSA', namedCurve: 'P-256' }, false, ['verify']);
  if (!await crypto.subtle.verify({ name: 'ECDSA', hash: 'SHA-256' }, key, decode(envelope.signature), payload)) throw new Error('Release signature verification failed.');
  const release = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(payload));
  const published = Date.parse(release.publishedAt), expires = Date.parse(release.expiresAt);
  if (release.schemaVersion !== 1 || release.product !== 'Sora' || release.channel !== channel || release.platform !== 'win-x64' ||
      !numericVersion(release.version) || !Number.isSafeInteger(release.sequence) || release.sequence < 1 ||
      !Number.isFinite(published) || !Number.isFinite(expires) || published > now + 300000 || expires <= now || expires <= published || expires - published > 45 * 86400000 ||
      !Number.isSafeInteger(release.size) || release.size < 1 || release.size > 2 * 1024 ** 3 || !/^[a-f0-9]{64}$/i.test(release.sha256) ||
      typeof release.notes !== 'string' || release.notes.length > 16000) throw new Error('Release metadata is expired or incompatible.');
  const url = new URL(release.installerUrl);
  if (url.protocol !== 'https:' || url.port || url.username || url.password || url.hash || url.search || !policy.allowedHosts.includes(url.hostname) ||
      url.pathname !== `/releases/${release.version}/win-x64/Sora-Setup-${release.version}-x64.exe`) throw new Error('The download address is not trusted.');
  return release;
}

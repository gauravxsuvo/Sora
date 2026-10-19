import { readFile, stat } from 'node:fs/promises';
import { createReadStream } from 'node:fs';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { storageConfig } from './storage.mjs';
import { verifyRelease } from './public/release-verification.mjs';

async function digest(stream, limit) {
  const hash = createHash('sha256'); let length = 0;
  for await (const chunk of stream) { length += chunk.length; if (length > limit) throw new Error('Artifact exceeds the signed size.'); hash.update(chunk); }
  return { length, hash: hash.digest('hex') };
}

try {
  const [mode, installerPath, feedPath] = process.argv.slice(2);
  if (!['upload', 'promote'].includes(mode) || !installerPath || !feedPath || process.argv.length !== 5)
    throw new Error('Usage: node publish-release.mjs upload|promote INSTALLER_PATH SIGNED_FEED_PATH');
  const config = storageConfig(process.env, { internal: false });
  if (!config) throw new Error('Set the S3 endpoint, bucket and separately revocable release-publisher access-key environment variables.');
  if (process.env.S3_PREFIX) throw new Error('Production publishing is not permitted with preview storage credentials.');
  const policy = JSON.parse(await readFile(new URL('./public/update-policy.json', import.meta.url), 'utf8'));
  const bytes = await readFile(feedPath);
  if (bytes.length > 131072) throw new Error('Oversized feed.');
  const release = await verifyRelease(JSON.parse(bytes), policy);
  const file = await stat(installerPath);
  const local = await digest(createReadStream(installerPath), release.size);
  if (!file.isFile() || local.length !== release.size || local.hash.toUpperCase() !== release.sha256.toUpperCase()) throw new Error('Installer does not match the signed feed.');
  const { S3Client, GetObjectCommand, PutObjectCommand, HeadObjectCommand } = await import('@aws-sdk/client-s3');
  const { Upload } = await import('@aws-sdk/lib-storage');
  const client = new S3Client({ ...config, maxAttempts: 4 });
  const key = config.prefix + new URL(release.installerUrl).pathname.slice(1);
  try {
    if (mode === 'upload') {
      let exists = false;
      try { await client.send(new HeadObjectCommand({ Bucket: config.bucket, Key: key })); exists = true; }
      catch (error) {
        // Some scoped S3 gateways return 403 for a missing object's HEAD even when PUT is allowed.
        // A conditional upload still cannot replace any existing versioned artifact.
        if (![403, 404].includes(error.$metadata?.httpStatusCode)) throw error;
      }
      if (!exists) {
        await new Upload({ client, queueSize: 2, partSize: 8 * 1024 * 1024, leavePartsOnError: false, params: {
          Bucket: config.bucket, Key: key, Body: createReadStream(installerPath), ContentLength: release.size,
          ContentType: 'application/octet-stream', CacheControl: 'public, max-age=31536000, immutable', IfNoneMatch: '*',
          Metadata: { sha256: release.sha256.toLowerCase() }
        } }).done();
        console.log('Versioned installer uploaded with conditional, multipart writes.');
      } else console.log('Installer key already exists; verifying exact public bytes before proceeding.');
    }
    const response = await fetch(release.installerUrl, { redirect: 'error', signal: AbortSignal.timeout(3600000) });
    if (!response.ok || !response.body) throw new Error(`Public download verification failed (${response.status}). Connect release storage to the website first.`);
    const remote = await digest(response.body, release.size);
    if (remote.length !== release.size || remote.hash.toUpperCase() !== release.sha256.toUpperCase()) throw new Error('Public artifact bytes do not match the signed release. Refusing promotion.');
    console.log(`Public installer verified: Sora ${release.version}.`);
    if (mode === 'promote') {
      const feedKey = config.prefix + 'channels/stable.json';
      let etag;
      try {
        const old = await client.send(new GetObjectCommand({ Bucket: config.bucket, Key: feedKey }));
        const oldBytes = await old.Body.transformToString();
        if (oldBytes.length > 131072) throw new Error('Existing feed is invalid. Inspect before publishing.');
        const oldEnvelope = JSON.parse(oldBytes);
        // Verify signature and policy even when refreshing an expired feed; do not reset its sequence.
        const oldExpiry = Date.parse(JSON.parse(Buffer.from(oldEnvelope.payload, 'base64').toString('utf8')).expiresAt);
        const previous = await verifyRelease(oldEnvelope, policy, Math.min(Date.now(), oldExpiry - 1));
        if (release.sequence <= previous.sequence) throw new Error('Promotion requires a strictly increasing feed sequence.');
        if (release.version === previous.version && release.sha256 !== previous.sha256) throw new Error('An existing release version cannot be replaced with different bytes.');
        etag = old.ETag;
        if (!etag) throw new Error('Storage did not return the ETag required for safe promotion.');
      } catch (error) {
        if (error.$metadata?.httpStatusCode === 403) {
          const publicFeed = await fetch(policy.feedUrl, { cache: 'no-store', redirect: 'error', signal: AbortSignal.timeout(30000) });
          if (publicFeed.status !== 404) throw new Error('Storage cannot read the existing feed. Inspect access before promotion.');
        } else if (error.$metadata?.httpStatusCode !== 404) throw error;
      }
      await client.send(new PutObjectCommand({ Bucket: config.bucket, Key: feedKey, Body: bytes,
        ContentType: 'application/json', CacheControl: 'no-store', ...(etag ? { IfMatch: etag } : { IfNoneMatch: '*' }) }));
      const promoted = await fetch(policy.feedUrl, { cache: 'no-store', redirect: 'error', signal: AbortSignal.timeout(30000) });
      if (!promoted.ok || !Buffer.from(await promoted.arrayBuffer()).equals(bytes)) throw new Error('Feed was written, but the public feed did not return matching bytes. Check cache/routing before retrying.');
      console.log(`Stable feed promoted to sequence ${release.sequence}. No installed client is forced to restart.`);
    } else console.log('Stable feed is unchanged. Qualify the installer, then run promote with the same signed feed.');
  } finally { client.destroy(); }
} catch (error) {
  // SDK error messages may include endpoint details. Do not serialize request objects or credentials.
  console.error(error.name === 'Error' ? error.message : `Release operation failed (${error.name}, HTTP ${error.$metadata?.httpStatusCode || 'unavailable'}). Check bucket access, endpoint and read/write permissions.`);
  process.exitCode = 1;
}

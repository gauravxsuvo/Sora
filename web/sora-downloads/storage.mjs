export function storageConfig(env = process.env, { internal = true } = {}) {
  const endpoint = internal ? env.S3_INTERNAL_ENDPOINT || env.S3_ENDPOINT : env.S3_ENDPOINT;
  if (!endpoint || !env.S3_BUCKET || !env.S3_ACCESS_KEY_ID || !env.S3_SECRET_ACCESS_KEY) return null;
  const prefix = env.S3_PREFIX || '';
  if (prefix.includes('..') || prefix.startsWith('/') || (prefix && !prefix.endsWith('/'))) throw new Error('Invalid storage prefix.');
  return {
    endpoint, region: env.S3_REGION || 'auto', forcePathStyle: env.S3_FORCE_PATH_STYLE !== 'false',
    credentials: { accessKeyId: env.S3_ACCESS_KEY_ID, secretAccessKey: env.S3_SECRET_ACCESS_KEY },
    bucket: env.S3_BUCKET, prefix: prefix + 'public/'
  };
}

export async function createStorage(config) {
  const { S3Client, GetObjectCommand, HeadObjectCommand } = await import('@aws-sdk/client-s3');
  const client = new S3Client({ ...config, maxAttempts: 3 });
  return {
    get: (key, { head, range, signal }) => client.send(new (head ? HeadObjectCommand : GetObjectCommand)({
      Bucket: config.bucket, Key: config.prefix + key, ...(range ? { Range: range } : {})
    }), { abortSignal: signal }),
    close: () => client.destroy()
  };
}

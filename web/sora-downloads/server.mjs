import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { join, resolve } from 'node:path';
import { pipeline } from 'node:stream/promises';
import { storageConfig, createStorage } from './storage.mjs';

const publicDirectory = fileURLToPath(new URL('./public/', import.meta.url));
const staticFiles = new Map([
  ['/', ['index.html', 'text/html; charset=utf-8']], ['/styles.css', ['styles.css', 'text/css; charset=utf-8']],
  ['/app.mjs', ['app.mjs', 'text/javascript; charset=utf-8']], ['/release-verification.mjs', ['release-verification.mjs', 'text/javascript; charset=utf-8']],
  ['/sora-mark.svg', ['sora-mark.svg', 'image/svg+xml']], ['/sora-updates.png', ['sora-updates.png', 'image/png']],
  ['/sora-mark.png', ['sora-mark.png', 'image/png']], ['/apple-touch-icon.png', ['apple-touch-icon.png', 'image/png']],
  ['/favicon.ico', ['favicon.ico', 'image/x-icon']],
  ['/update-policy.json', ['update-policy.json', 'application/json']], ['/robots.txt', ['robots.txt', 'text/plain']],
  ['/sitemap.xml', ['sitemap.xml', 'application/xml']]
]);

export function makeServer({ storage = null, logger = console } = {}) {
  return createServer(async (req, res) => {
    res.setHeader('X-Content-Type-Options', 'nosniff');
    res.setHeader('Referrer-Policy', 'strict-origin-when-cross-origin');
    res.setHeader('Content-Security-Policy', "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'");
    if (!['GET', 'HEAD'].includes(req.method)) { res.writeHead(405, { Allow: 'GET, HEAD' }); res.end(); return; }
    try {
      const path = new URL(req.url, 'http://localhost').pathname;
      if (path === '/healthz') { res.writeHead(200, { 'Content-Type': 'text/plain' }); res.end(req.method === 'HEAD' ? undefined : 'ok'); return; }
      const staticFile = staticFiles.get(path);
      if (staticFile) {
        const bytes = await readFile(join(publicDirectory, staticFile[0]));
        res.writeHead(200, { 'Content-Type': staticFile[1], 'Content-Length': bytes.length, 'Cache-Control': 'no-cache' });
        res.end(req.method === 'HEAD' ? undefined : bytes); return;
      }
      const feed = path === '/channels/stable.json' || path === '/channels/beta.json';
      const installer = /^\/releases\/(\d{1,5}\.\d{1,5}\.\d{1,5}(?:-beta\.[1-9]\d{0,4})?)\/win-x64\/Sora-Setup-\1-x64\.exe$/.exec(path);
      if (!feed && !installer) { res.writeHead(404); res.end('Not found'); return; }
      if (!storage) { res.writeHead(503, { 'Retry-After': '300', 'Cache-Control': 'no-store' }); res.end('Release storage is not connected yet.'); return; }
      const range = !feed ? req.headers.range : undefined;
      if (range && !/^bytes=\d*-\d*$/.test(range)) { res.writeHead(416); res.end(); return; }
      const abort = new AbortController();
      res.on('close', () => { if (!res.writableFinished) abort.abort(); });
      const object = await storage.get(path.slice(1), { head: req.method === 'HEAD', range, signal: abort.signal });
      const headers = {
        'Content-Type': feed ? 'application/json' : 'application/octet-stream',
        'Cache-Control': feed ? 'no-store' : 'public, max-age=31536000, immutable',
        'Accept-Ranges': feed ? 'none' : 'bytes'
      };
      if (object.ContentLength != null) headers['Content-Length'] = String(object.ContentLength);
      if (object.ETag) headers.ETag = object.ETag;
      if (object.ContentRange) headers['Content-Range'] = object.ContentRange;
      if (!feed) headers['Content-Disposition'] = `attachment; filename="Sora-Setup-${installer[1]}-x64.exe"`;
      res.writeHead(object.ContentRange ? 206 : 200, headers);
      if (req.method === 'HEAD') { res.end(); return; }
      await pipeline(object.Body, res);
    } catch (error) {
      if (res.headersSent) { res.destroy(); return; }
      const status = error.$metadata?.httpStatusCode;
      const missing = status === 404 || error.code === 'ENOENT';
      const limited = status === 429 || error.name === 'SlowDown';
      res.writeHead(limited ? 429 : missing ? 404 : 503, { 'Cache-Control': 'no-store', 'Retry-After': '60' });
      res.end(missing ? 'Release not found.' : 'Downloads are temporarily unavailable. Please try again.');
      // Log the class/status only: never dump SDK requests or storage credentials.
      if (!missing) logger.error('Download request failed:', error.name, status || 'unavailable');
    }
  });
}

if (process.argv[1] && fileURLToPath(import.meta.url) === resolve(process.argv[1])) {
  const config = storageConfig();
  const storage = config ? await createStorage(config) : null;
  const server = makeServer({ storage });
  const port = Number(process.env.PORT || 3000);
  server.listen(port, '0.0.0.0', () => console.log(`Sora downloads listening on port ${port}. Storage ${storage ? 'connected' : 'not configured'}.`));
  const shutdown = () => { server.close(() => { storage?.close(); process.exit(0); }); };
  process.on('SIGTERM', shutdown); process.on('SIGINT', shutdown);
}

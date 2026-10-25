/**
 * @fileoverview Minimal static file server for the runtime benchmark fixtures.
 * @module scripts/benchmark/server
 *
 * Serves the repository root so fixture pages can load the local Hawk build via relative paths.
 * Started automatically by Playwright's `webServer` option in `playwright.config.mjs`.
 */

import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { extname, join, normalize, resolve } from 'node:path';

const ROOT = resolve(process.cwd(), process.env.SITE_ROOT ?? '_site');
const PORT = Number(process.env.BENCHMARK_PORT ?? 4173);

const CONTENT_TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.map': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.ico': 'image/x-icon',
  '.woff2': 'font/woff2',
  '.xml': 'application/xml; charset=utf-8',
  '.txt': 'text/plain; charset=utf-8',
};

const server = createServer(async (req, res) => {
  const urlPath = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
  const filePath = normalize(join(ROOT, urlPath));

  if (!filePath.startsWith(ROOT)) {
    res.writeHead(403);
    res.end('Forbidden');
    return;
  }

  try {
    let servedPath = filePath;
    let stats = await stat(servedPath);
    if (stats.isDirectory()) {
      // Directory-index resolution — every real static host (GitLab Pages, Netlify, nginx,
      // Hugo's own dev server) does this automatically; without it, a directory-style URL
      // (Pagefind's own item.url values are directory-style, no trailing /index.html) 404s here
      // even though it's a completely normal URL everywhere else, silently masking real
      // fetch-path bugs behind hawk's excerpt fallback instead of actually exercising the fetch.
      servedPath = join(servedPath, 'index.html');
      stats = await stat(servedPath);
    }
    const body = await readFile(servedPath);
    res.writeHead(200, { 'Content-Type': CONTENT_TYPES[extname(servedPath)] ?? 'application/octet-stream' });
    res.end(body);
  } catch {
    res.writeHead(404);
    res.end('Not found');
  }
});

server.listen(PORT, () => {
  console.log(`Benchmark fixture server listening on http://localhost:${PORT}`);
});

#!/usr/bin/env node

/**
 * Verifies responsive alignment and layout flow of the built `_site/` across
 * Mobile (390x844), Tablet (1024x768), and Desktop (1440x900) viewports using
 * headless Chromium when Playwright is available.
 */

import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const rootDir = resolve(__dirname, '../..');

const VIEWPORTS = [
  { name: 'Desktop', width: 1440, height: 900, expectedTocDisplay: 'block', expectedMenuDisplay: 'none' },
  { name: 'Tablet', width: 1024, height: 768, expectedTocDisplay: 'none', expectedMenuDisplay: 'none' },
  { name: 'Mobile', width: 390, height: 844, expectedTocDisplay: 'none', expectedMenuDisplay: 'flex' },
];

const ROUTES = ['/', '/getting-started/', '/cli-reference/', '/fluxfile/', '/data-and-telemetry/', '/security/', '/concept-inventory/', '/adr/'];

async function loadPlaywright() {
  const candidates = [
    'playwright',
    '/home/gsnow/.npm/_npx/e41f203b7505f1fb/node_modules/playwright/index.mjs',
  ];
  for (const modPath of candidates) {
    try {
      return await import(modPath);
    } catch (_) {}
  }
  return null;
}

const pw = await loadPlaywright();
if (!pw || !existsSync(resolve(rootDir, '_site/index.html'))) {
  console.log('Skipping headless browser check (Playwright or _site not present).');
  process.exit(0);
}

const port = 4179;
const serverProc = spawn(process.execPath, [resolve(__dirname, 'server.mjs')], {
  cwd: rootDir,
  env: { ...process.env, BENCHMARK_PORT: String(port), SITE_ROOT: '_site' },
  stdio: ['ignore', 'pipe', 'pipe'],
});

await new Promise((resolveReady, rejectReady) => {
  const timer = setTimeout(() => rejectReady(new Error('Server startup timed out')), 5000);
  serverProc.stdout.on('data', (chunk) => {
    if (String(chunk).includes('listening')) {
      clearTimeout(timer);
      resolveReady();
    }
  });
});

let browser;
try {
  browser = await pw.chromium.launch({ headless: true });
  const context = await browser.newContext();
  const page = await context.newPage();

  for (const vp of VIEWPORTS) {
    await page.setViewportSize({ width: vp.width, height: vp.height });

    for (const route of ROUTES) {
      await page.goto(`http://localhost:${port}${route}`, { waitUntil: 'networkidle' });

      const metrics = await page.evaluate(() => {
        const docWidth = document.documentElement.scrollWidth;
        const winWidth = window.innerWidth;
        const toc = document.querySelector('.flux-toc');
        const menuBtn = document.querySelector('.flux-header__menu-btn');
        const main = document.querySelector('.flux-main');
        const mainRect = main?.getBoundingClientRect();

        return {
          overflowX: docWidth - winWidth,
          tocDisplay: toc ? getComputedStyle(toc).display : 'none',
          menuDisplay: menuBtn ? getComputedStyle(menuBtn).display : 'none',
          mainWidth: mainRect ? Math.round(mainRect.width) : 0,
        };
      });

      if (metrics.overflowX > 1) {
        throw new Error(`${vp.name} (${vp.width}x${vp.height}) ${route}: horizontal overflow of ${metrics.overflowX}px`);
      }
      if (metrics.tocDisplay !== vp.expectedTocDisplay) {
        throw new Error(`${vp.name} ${route}: expected .flux-toc display=${vp.expectedTocDisplay}, got ${metrics.tocDisplay}`);
      }
      if (metrics.menuDisplay !== vp.expectedMenuDisplay) {
        throw new Error(`${vp.name} ${route}: expected menu button display=${vp.expectedMenuDisplay}, got ${metrics.menuDisplay}`);
      }
    }

    // On Mobile, also verify opening & closing the off-canvas navigation drawer
    if (vp.name === 'Mobile') {
      await page.goto(`http://localhost:${port}/`, { waitUntil: 'networkidle' });
      await page.click('[data-drawer-toggle]');
      const isOpen = await page.evaluate(() =>
        document.getElementById('flux-sidebar-drawer')?.classList.contains('is-open')
      );
      if (!isOpen) {
        throw new Error('Mobile drawer did not open when [data-drawer-toggle] was clicked.');
      }
      await page.click('[data-drawer-close]');
    }

    console.log(`  [PASS] ${vp.name} (${vp.width}x${vp.height}) verified across ${ROUTES.length} routes (0px horizontal overflow)`);
  }
} finally {
  await browser?.close();
  serverProc.kill();
}

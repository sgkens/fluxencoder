#!/usr/bin/env node

/**
 * Validates the generated 11ty documentation site in `_site/`:
 * - Confirms all expected pages, assets, JSON Schema, and Pagefind search bundle exist
 * - Verifies Nunjucks component markers (`data-component="..."`) on every page
 * - Verifies Pagefind indexing boundaries (`data-pagefind-body` and `data-pagefind-ignore`)
 * - Verifies all internal `href="/..."` links resolve to built files
 * - Verifies responsive CSS breakpoints (desktop 3-column, tablet 2-column, mobile 1-column drawer)
 * - Verifies zero emoji characters across all generated HTML pages
 */

import { existsSync } from 'node:fs';
import { readFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const rootDir = resolve(__dirname, '..');
const siteDir = resolve(rootDir, '_site');

const EXPECTED_PAGES = [
  'index.html',
  'getting-started/index.html',
  'cli-reference/index.html',
  'fluxfile/index.html',
  'data-and-telemetry/index.html',
  'security/index.html',
  'concept-inventory/index.html',
  'adr/index.html',
  'adr/0001-target-framework-policy/index.html',
  'adr/0002-cli-framework-and-package-policy/index.html',
  'adr/0003-release-artifact-policy/index.html',
];

const EXPECTED_ASSETS = [
  'assets/css/flux-docs.css',
  'assets/js/flux-docs.js',
  'schema/fluxfile-v1.schema.json',
  'pagefind/pagefind.js',
];

const REQUIRED_SHELL_COMPONENTS = [
  'data-component="site-header"',
  'data-component="app-shell"',
  'data-component="doc-sidebar"',
  'data-component="page-toc"',
  'data-component="site-footer"',
  'data-component="search-modal"',
];

// Emoji ranges (pictographs, emoticons, transport, supplemental symbols)
const EMOJI_REGEX = /[\u{1F300}-\u{1F9FF}\u{1FA70}-\u{1FAFF}]/u;

const failures = [];

function check(label, ok, detail = '') {
  if (ok) {
    console.log(`  [PASS] ${label}${detail ? ` — ${detail}` : ''}`);
  } else {
    console.error(`  [FAIL] ${label}${detail ? ` — ${detail}` : ''}`);
    failures.push(`${label}${detail ? `: ${detail}` : ''}`);
  }
}

console.log('=== FluxEncoder Documentation Site Verification ===\n');

for (const asset of EXPECTED_ASSETS) {
  const fullPath = resolve(siteDir, asset);
  check(`Asset ${asset}`, existsSync(fullPath));
}

const cssPath = resolve(siteDir, 'assets/css/flux-docs.css');
if (existsSync(cssPath)) {
  const css = await readFile(cssPath, 'utf8');
  check(
    'Responsive CSS Grid shell (Desktop / Tablet / Mobile)',
    css.includes('grid-template-columns: var(--flux-sidebar-w) minmax(0, 1fr) var(--flux-toc-w)') &&
      css.includes('@media (max-width: 1200px)') &&
      css.includes('@media (max-width: 900px)') &&
      css.includes('@media (max-width: 640px)')
  );
}

for (const pageRel of EXPECTED_PAGES) {
  const fullPath = resolve(siteDir, pageRel);
  const exists = existsSync(fullPath);
  check(`Page ${pageRel}`, exists);
  if (!exists) continue;

  const html = await readFile(fullPath, 'utf8');

  // Verify shell components
  const missingComponents = REQUIRED_SHELL_COMPONENTS.filter((marker) => !html.includes(marker));
  check(
    `${pageRel}: Nunjucks component architecture`,
    missingComponents.length === 0,
    missingComponents.length > 0 ? `missing ${missingComponents.join(', ')}` : 'all shell macros present'
  );

  // Verify Pagefind attributes
  check(
    `${pageRel}: Pagefind attributes`,
    html.includes('data-pagefind-body') && html.includes('data-pagefind-ignore="all"')
  );

  // Verify zero emojis
  check(`${pageRel}: Zero emojis (geometric glyphs only)`, !EMOJI_REGEX.test(html));

  // Verify internal links resolve
  const hrefRegex = /href="(\/[^"#?]*)"/g;
  let match;
  const brokenLinks = [];
  while ((match = hrefRegex.exec(html)) !== null) {
    const targetUrl = match[1];
    const targetPath = targetUrl.endsWith('/')
      ? join(siteDir, targetUrl, 'index.html')
      : join(siteDir, targetUrl);
    if (!existsSync(targetPath)) {
      brokenLinks.push(targetUrl);
    }
  }
  check(
    `${pageRel}: Internal links resolve`,
    brokenLinks.length === 0,
    brokenLinks.length > 0 ? `broken: ${brokenLinks.join(', ')}` : 'OK'
  );
}

console.log('');
if (failures.length > 0) {
  console.error(`Site validation failed with ${failures.length} error(s).`);
  process.exit(1);
}

console.log(`All ${EXPECTED_PAGES.length} pages and ${EXPECTED_ASSETS.length} assets passed verification.`);

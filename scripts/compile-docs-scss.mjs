#!/usr/bin/env node

/**
 * Bundles the modular CSS partials in `site/styles/` into `site/assets/css/flux-docs.css`.
 * Keeps strict separation of concerns across design tokens, base reset, layout shell,
 * Nunjucks component styles, and responsive utilities without requiring external native binaries.
 */

import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const rootDir = resolve(__dirname, '..');

const PARTIALS = [
  'site/styles/_tokens.css',
  'site/styles/_base.css',
  'site/styles/_layout.css',
  'site/styles/_components.css',
  'site/styles/_utilities.css',
];

const outputCssPath = resolve(rootDir, 'site/assets/css/flux-docs.css');

const chunks = [
  '/*',
  ' * FluxEncoder Documentation Stylesheet (Generated)',
  ' * Source: site/styles/{_tokens,_base,_layout,_components,_utilities}.css',
  ' * Do not edit flux-docs.css directly; run `node scripts/compile-docs-scss.mjs`.',
  ' */',
];

for (const relPath of PARTIALS) {
  const fullPath = resolve(rootDir, relPath);
  const content = await readFile(fullPath, 'utf8');
  chunks.push(`\n/* === [${relPath}] === */\n${content.trim()}`);
}

await mkdir(dirname(outputCssPath), { recursive: true });
await writeFile(outputCssPath, `${chunks.join('\n')}\n`, 'utf8');

console.log(`Compiled ${PARTIALS.length} CSS modules -> site/assets/css/flux-docs.css`);

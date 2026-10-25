#!/usr/bin/env node

import { existsSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const rootDir = resolve(__dirname, '..');

function findPagefindBinary() {
  const executable = process.platform === 'win32' ? 'pagefind.cmd' : 'pagefind';
  const localBin = resolve(rootDir, 'node_modules/.bin', executable);
  return existsSync(localBin) ? localBin : null;
}

const args = process.argv.slice(2);
const hasSiteArg = args.some((arg, index) => arg === '--site' || arg.startsWith('--site=') || args[index - 1] === '--site');
const siteDir = hasSiteArg ? null : resolve(rootDir, '_site');
const pagefindArgs = hasSiteArg ? args : ['--site', siteDir, ...args];

if (siteDir && !existsSync(siteDir)) {
  console.error(`Pagefind site directory not found: ${siteDir}`);
  console.error('Build the 11ty documentation site before indexing with Pagefind.');
  process.exit(1);
}

const localBin = findPagefindBinary();
const command = localBin ?? (process.platform === 'win32' ? 'npx.cmd' : 'npx');
const commandArgs = localBin ? pagefindArgs : ['--yes', 'pagefind', ...pagefindArgs];
const result = spawnSync(command, commandArgs, {
  cwd: rootDir,
  stdio: 'inherit',
  shell: process.platform === 'win32',
});

if (result.error) {
  console.error(`Failed to run Pagefind: ${result.error.message}`);
  process.exit(1);
}

process.exit(result.status ?? 1);

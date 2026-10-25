import { defineConfig } from '@playwright/test';

const PORT = 4173;

export default defineConfig({
  testDir: '.',
  testMatch: '**/*.spec.mjs',
  timeout: 30_000,
  fullyParallel: false,
  workers: 1,
  reporter: 'list',
  webServer: {
    command: 'node scripts/benchmark/server.mjs',
    port: PORT,
    reuseExistingServer: !process.env.CI,
    cwd: process.cwd(),
    env: { BENCHMARK_PORT: String(PORT) },
  },
  use: {
    baseURL: `http://localhost:${PORT}`,
  },
});

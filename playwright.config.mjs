import { defineConfig } from '@playwright/test';
// PW_CHANNEL=chrome でインストール済みの Google Chrome を使う（Windows で Chromium を落とさずに済む）
export default defineConfig({
  testDir: './test',
  testMatch: /.*\.spec\.mjs/,
  timeout: 30000,
  workers: 1,
  use: { browserName: 'chromium', headless: true, ...(process.env.PW_CHANNEL ? { channel: process.env.PW_CHANNEL } : {}) },
  reporter: 'list',
});

// Playwright テスト: 埋め込み（R-11: アーティファクト相当の sandbox iframe）
import { test, expect } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { useHost, ROOT } from './helpers.mjs';

useHost(test);

// ---------- verify（2026-10-07）の指摘への対応と補強 ----------
test('R-11: 埋め込み版をアーティファクト相当の sandbox iframe で使える', async ({ page }) => {
  const { execFileSync } = await import('node:child_process');
  const { mkdtempSync, copyFileSync } = await import('node:fs');
  const { tmpdir } = await import('node:os');
  const dir = mkdtempSync(join(tmpdir(), 'fb-art-'));
  const html = join(dir, 'art.html');
  copyFileSync(join(ROOT, 'test', 'plain.html'), html);
  execFileSync(process.execPath, [join(ROOT, 'tools', 'embed-artifact.mjs'), html]);
  const { readFileSync } = await import('node:fs');
  const srcdoc = readFileSync(html, 'utf8');
  await page.setContent('<iframe id="a" sandbox="allow-scripts allow-same-origin allow-forms allow-popups" allow="clipboard-write" style="width:900px;height:600px;border:0"></iframe>');
  await page.evaluate((s) => { document.getElementById('a').srcdoc = s; }, srcdoc);
  const frame = page.frameLocator('#a');
  await expect(frame.locator('button.fb-draw')).toBeVisible();
  const f = page.frames().find((x) => x !== page.mainFrame());
  await frame.locator('button.fb-draw').click();
  const fb = await page.locator('#a').boundingBox();
  await page.mouse.move(fb.x + 20, fb.y + 20); await page.mouse.down(); await page.mouse.move(fb.x + 300, fb.y + 120, { steps: 3 }); await page.mouse.up();
  await expect(frame.locator('textarea.fb-input')).toBeFocused();
  await page.keyboard.type('埋め込み'); await page.keyboard.press('Enter');
  await page.keyboard.press('Control+Enter');
  await f.waitForFunction(() => (window.__feedbackTool.lastPayload && window.__feedbackTool.state === 'idle') || !!document.querySelector('.fb-panel'));
  const p = await f.evaluate(() => ({ png: !!window.__feedbackTool.lastPayload.png, text: window.__feedbackTool.lastPayload.text }));
  expect(p.png).toBe(true);
  expect(p.text).toContain('1. 埋め込み');
});

// H-3 検証: 実画面のスクリーンショットと、feedback.js（html2canvas）が作る画像を比べる
//
// 使い方:
//   node tools/h3-compare.mjs <名前> <URL または HTML ファイル> [--wait 2000] [--chrome]
//   例) node tools/h3-compare.mjs slide-review http://127.0.0.1:8765/review --chrome
//       node tools/h3-compare.mjs host test/host.html
//
// 出力: docs/h3/<名前>-real.png, <名前>-h2c.png, <名前>-diff.png と、差分率（RGB の最大差が 32 を超える画素の割合）
// 対象ページのファイルは変更しない（ページ内に dist/feedback.js を注入するだけ）。
import { chromium } from '@playwright/test';
import { mkdirSync, writeFileSync, existsSync } from 'node:fs';
import { resolve, join, dirname } from 'node:path';
import { pathToFileURL, fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const args = process.argv.slice(2);
const flag = (k) => args.includes(k);
const opt = (k, d) => { const i = args.indexOf(k); return i >= 0 ? args[i + 1] : d; };
const positional = args.filter((a, i) => !a.startsWith('--') && !(i > 0 && args[i - 1] === '--wait'));
const [name, target] = positional;
if (!name || !target) { console.error('usage: node tools/h3-compare.mjs <name> <url|file> [--wait ms] [--chrome]'); process.exit(2); }

const url = /^https?:|^file:/.test(target) ? target : pathToFileURL(resolve(target)).href;
const outDir = join(ROOT, 'docs', 'h3');
mkdirSync(outDir, { recursive: true });
const bundle = join(ROOT, 'dist', 'feedback.js');
if (!existsSync(bundle)) { console.error('dist/feedback.js がありません。先に node build.mjs を実行してください'); process.exit(2); }

const useChrome = flag('--chrome') || process.env.PW_CHANNEL === 'chrome';
const browser = await chromium.launch({ headless: true, ...(useChrome ? { channel: 'chrome' } : {}) });
try {
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 }, deviceScaleFactor: 1 });
  await page.goto(url, { waitUntil: 'networkidle' }).catch(() => page.goto(url));
  await page.waitForTimeout(Number(opt('--wait', 1500)));

  const realBuf = await page.screenshot({ type: 'png' });
  writeFileSync(join(outDir, `${name}-real.png`), realBuf);

  await page.addScriptTag({ path: bundle });
  // 注入したツール自身のボタン類は撮影対象外（data-fb-ignore）。ここでは非表示にして実画面と条件をそろえる
  await page.addStyleTag({ content: '.fb-bar{display:none !important}' });
  const h2cDataUrl = await page.evaluate(async () => {
    const c = await window.html2canvas(document.documentElement, {
      x: window.scrollX, y: window.scrollY, width: innerWidth, height: innerHeight,
      windowWidth: innerWidth, windowHeight: innerHeight, scale: 1, useCORS: true, logging: false,
      ignoreElements: (el) => el.nodeType === 1 && el.hasAttribute('data-fb-ignore'),
    });
    return c.toDataURL('image/png');
  });
  const h2cBuf = Buffer.from(h2cDataUrl.split(',')[1], 'base64');
  writeFileSync(join(outDir, `${name}-h2c.png`), h2cBuf);

  // 差分はブラウザ内で計算（追加の依存を入れない）
  const res = await page.evaluate(async ([a, b]) => {
    const load = (src) => new Promise((ok, ng) => { const i = new Image(); i.onload = () => ok(i); i.onerror = ng; i.src = src; });
    const [ia, ib] = await Promise.all([load(a), load(b)]);
    const w = Math.min(ia.width, ib.width), h = Math.min(ia.height, ib.height);
    const ca = document.createElement('canvas'); ca.width = w; ca.height = h; const xa = ca.getContext('2d'); xa.drawImage(ia, 0, 0);
    const cb = document.createElement('canvas'); cb.width = w; cb.height = h; const xb = cb.getContext('2d'); xb.drawImage(ib, 0, 0);
    const da = xa.getImageData(0, 0, w, h).data, db = xb.getImageData(0, 0, w, h).data;
    const cd = document.createElement('canvas'); cd.width = w; cd.height = h; const xd = cd.getContext('2d');
    const out = xd.createImageData(w, h); let diff = 0;
    for (let p = 0; p < da.length; p += 4) {
      const d = Math.max(Math.abs(da[p] - db[p]), Math.abs(da[p + 1] - db[p + 1]), Math.abs(da[p + 2] - db[p + 2]));
      const g = (da[p] + da[p + 1] + da[p + 2]) / 3;
      if (d > 32) { diff++; out.data[p] = 229; out.data[p + 1] = 57; out.data[p + 2] = 53; out.data[p + 3] = 255; }
      else { out.data[p] = out.data[p + 1] = out.data[p + 2] = 255 - (255 - g) * 0.25; out.data[p + 3] = 255; }
    }
    xd.putImageData(out, 0, 0);
    return { w, h, sizeA: [ia.width, ia.height], sizeB: [ib.width, ib.height], ratio: diff / (w * h), diffPng: cd.toDataURL('image/png') };
  }, ['data:image/png;base64,' + realBuf.toString('base64'), h2cDataUrl]);
  writeFileSync(join(outDir, `${name}-diff.png`), Buffer.from(res.diffPng.split(',')[1], 'base64'));

  const pct = (res.ratio * 100).toFixed(2);
  const verdict = res.ratio < 0.02 ? 'ほぼ一致' : res.ratio < 0.10 ? '部分的な差（文字の描画差・影など）' : '大きな差（要目視）';
  console.log(JSON.stringify({ name, url, real: res.sizeA, h2c: res.sizeB, diffRatio: Number(pct), verdict, out: `docs/h3/${name}-{real,h2c,diff}.png` }, null, 2));
} finally {
  await browser.close();
}

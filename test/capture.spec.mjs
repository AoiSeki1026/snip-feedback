// Playwright テスト: 画像化（H-3 の placeholder、iframe の差し替えとレイアウト、R-13、H-M08）
import { test, expect } from '@playwright/test';
import { useHost, base, sendAll, annotate, flexCase, greenUrl, pixelsOf, pngServer, columnCase } from './helpers.mjs';

useHost(test);

test('H-3: textarea の placeholder（空欄の案内文）も送信画像に描かれ、実画面の入力欄は変わらない', async ({ page }) => {
  const box = await page.locator('#memo').boundingBox();
  await annotate(page, box.x - 10, box.y - 10, box.x + box.width + 10, box.y + box.height + 10, '案内文');
  await sendAll(page);
  expect(await page.locator('#memo').inputValue()).toBe('');   // 実画面は変えない（複製側だけ）
  const dark = await page.evaluate(async (b) => {
    const bmp = await createImageBitmap(window.__feedbackTool.lastPayload.png);
    const c = new OffscreenCanvas(bmp.width, bmp.height); const x = c.getContext('2d'); x.drawImage(bmp, 0, 0);
    const d = x.getImageData(Math.round(b.x + 4), Math.round(b.y + 3), Math.round(b.width * 0.5), Math.round(b.height - 6)).data;
    let n = 0; for (let i = 0; i < d.length; i += 4) if (d[i] < 200 && d[i + 1] < 200 && d[i + 2] < 200) n++;
    return n;
  }, box);
  expect(dark).toBeGreaterThan(20);   // 案内文の文字の画素がある
});

// ---------- verify r4（2026-10-07）の差し戻し対応 ----------
test('R-13 r4: ホストが HTMLIFrameElement.prototype を凍結していても送信でき、外した src は戻る', async ({ page }) => {
  await page.evaluate(() => {
    const b = document.createElement('img'); b.id = 'lazy'; b.loading = 'lazy'; b.src = '/nothing/lazy.png'; b.style.cssText = 'margin-top:6000px;display:block';
    document.body.append(b);
    Object.freeze(HTMLIFrameElement.prototype);
  });
  await annotate(page, 30, 90, 300, 200, '凍結');
  await sendAll(page);
  const p = await page.evaluate(() => ({ png: !!window.__feedbackTool.lastPayload.png, err: window.__feedbackTool.lastPayload.captureError, text: window.__feedbackTool.lastPayload.text, src: document.getElementById('lazy').getAttribute('src') }));
  expect(p.text).toContain('1. 凍結');
  expect(p.png).toBe(true);
  expect(p.err).toBeNull();
  expect(p.src).toBe('/nothing/lazy.png');
});

test('R-18 r4: CSS で大きさを決めた iframe も、画像では灰色の箱になる', async ({ page }) => {
  await page.evaluate(() => {
    const st = document.createElement('style'); st.textContent = '#cssf{position:absolute;left:700px;top:420px;width:240px;height:120px;border:0}'; document.head.append(st);
    const f = document.createElement('iframe'); f.id = 'cssf'; f.srcdoc = '<body style="background:#fff">x</body>'; document.body.append(f);
  });
  await page.waitForFunction(() => document.getElementById('cssf').contentDocument?.readyState === 'complete');
  await annotate(page, 30, 90, 300, 200, '箱');
  await sendAll(page);
  const px = await page.evaluate(async () => {
    const bmp = await createImageBitmap(window.__feedbackTool.lastPayload.png);
    const c = new OffscreenCanvas(bmp.width, bmp.height); const x = c.getContext('2d'); x.drawImage(bmp, 0, 0);
    return Array.from(x.getImageData(710, 430, 1, 1).data);
  });
  expect(px.slice(0, 3)).toEqual([224, 224, 224]);   // #e0e0e0
});

// ---------- verify r4b（2026-10-07）の差し戻し対応 ----------
test('R-18/R-13 r4b: iframe の差し替えができないホストで iframe があれば、画像を作らずテキストのみ送り、iframe を読み直さない', async ({ page }) => {
  await page.evaluate((b) => {
    const f = document.createElement('iframe'); f.id = 'same'; f.src = b + '/test/plain.html'; f.style.cssText = 'position:absolute;left:700px;top:420px;width:200px;height:100px';
    document.body.append(f);
  }, base);
  await page.waitForFunction(() => document.getElementById('same').contentDocument?.readyState === 'complete');
  await page.evaluate(() => Object.freeze(HTMLIFrameElement.prototype));
  const reqs = [];
  page.on('request', (r) => reqs.push(r.url()));
  await annotate(page, 30, 90, 300, 200, '凍結iframe');
  await sendAll(page);
  const p = await page.evaluate(() => window.__feedbackTool.lastPayload);
  expect(p.png).toBeNull();
  expect(p.clipboard.text).toBe(true);
  await expect(page.locator('.fb-toast')).toContainText('画像なしで送信しました（1件）');
  expect(reqs).toEqual([]);
});

test('R-18 r4b: iframe の padding・border・box-sizing で、後ろの要素が画像の中でずれない', async ({ page }) => {
  await page.evaluate(() => {
    const st = document.createElement('style');
    st.textContent = '#rowA,#rowB{position:absolute;left:20px;line-height:0;white-space:nowrap} #rowA{top:420px} #rowB{top:600px} #rowB *{box-sizing:border-box}'
      + ' .mk{display:inline-block;width:40px;height:40px;background:#0000ff;vertical-align:top}';
    document.head.append(st);
    const a = document.createElement('div'); a.id = 'rowA';
    a.innerHTML = '<iframe width="200" height="60" style="padding:10px;border:6px solid #888" srcdoc="x"></iframe><span class="mk" id="mkA"></span>';
    const b = document.createElement('div'); b.id = 'rowB';
    b.innerHTML = '<iframe srcdoc="y" style="height:60px"></iframe><span class="mk" id="mkB"></span>';
    document.body.append(a, b);
  });
  await page.waitForTimeout(300);
  const rA = await page.locator('#mkA').boundingBox();
  const rB = await page.locator('#mkB').boundingBox();
  await annotate(page, 30, 90, 300, 200, 'ずれ');
  await sendAll(page);
  const px = await page.evaluate(async (pts) => {
    const bmp = await createImageBitmap(window.__feedbackTool.lastPayload.png);
    const c = new OffscreenCanvas(bmp.width, bmp.height); const x = c.getContext('2d'); x.drawImage(bmp, 0, 0);
    return pts.map(([px, py]) => Array.from(x.getImageData(px, py, 1, 1).data).slice(0, 3));
  }, [[rA.x + 1, rA.y + 20], [rA.x + rA.width - 2, rA.y + 20], [rB.x + 1, rB.y + 20], [rB.x + rB.width - 2, rB.y + 20]].map(([a, b]) => [Math.round(a), Math.round(b)]));
  for (const c of px) expect(c).toEqual([0, 0, 255]);
});

// ---------- verify r4c（2026-10-07）への対応・r5 ----------
test('R-18 r4c: flex で伸び縮みする iframe の隣の要素が、画像の中でずれない', async ({ page }) => {
  await page.evaluate(() => {
    const st = document.createElement('style');
    // verify-r4c の再現条件: 容器 300px、iframe と兄弟がどちらも flex-basis auto（幅指定あり）で伸びる
    st.textContent = '#flexrow{position:absolute;left:20px;top:420px;width:300px;display:flex;align-items:flex-start}'
      + ' #flexrow iframe{flex:1 1 auto;width:100px;height:60px;border:0} #flexrow .mk{flex:1 1 auto;width:40px;height:40px;background:#0000ff}';
    document.head.append(st);
    const r = document.createElement('div'); r.id = 'flexrow';
    r.innerHTML = '<iframe srcdoc="x"></iframe><div class="mk" id="mkF"></div>';
    document.body.append(r);
  });
  await page.waitForTimeout(300);
  const m = await page.locator('#mkF').boundingBox();
  await annotate(page, 30, 90, 300, 200, 'flex');
  await sendAll(page);
  const px = await page.evaluate(async (pts) => {
    const bmp = await createImageBitmap(window.__feedbackTool.lastPayload.png);
    const c = new OffscreenCanvas(bmp.width, bmp.height); const x = c.getContext('2d'); x.drawImage(bmp, 0, 0);
    return pts.map(([px, py]) => Array.from(x.getImageData(px, py, 1, 1).data).slice(0, 3));
  }, [[m.x + 1, m.y + 20], [m.x + m.width - 2, m.y + 20]].map(([a, b]) => [Math.round(a), Math.round(b)]));
  expect(px[0]).toEqual([0, 0, 255]);   // 兄弟の左端・右端が実画面と同じ位置にある
  expect(px[1]).toEqual([0, 0, 255]);
});

// ---------- verify r5（2026-10-07）の差し戻し対応 ----------
test('R-18 r5: flex で縮む iframe の隣の要素も、画像の中でずれない', async ({ page }) => {
  const px = await flexCase(
    '#flexrow2{position:absolute;left:20px;top:420px;width:200px;display:flex;align-items:flex-start} #flexrow2 iframe{min-width:0;height:60px;border:0} #flexrow2 .mk{flex:0 1 100px;height:40px;background:#0000ff}',
    '<iframe width="300" srcdoc="x"></iframe><div class="mk" id="mkG"></div>')(page);
  expect(px[0]).toEqual([0, 0, 255]); expect(px[1]).toEqual([0, 0, 255]);
  expect(px[2]).not.toEqual([0, 0, 255]);   // 兄弟が左へずれていない（すぐ左は iframe 側）
});

test('R-18 r5: ホスト CSS が iframe の flex に !important を付けていても、隣の要素がずれない', async ({ page }) => {
  const px = await flexCase(
    '#flexrow2{position:absolute;left:20px;top:420px;width:300px;display:flex;align-items:flex-start} #flexrow2 iframe{flex:1 1 auto !important;width:100px;height:60px;border:0} #flexrow2 .mk{flex:1 1 auto;width:40px;height:40px;background:#0000ff}',
    '<iframe srcdoc="x"></iframe><div class="mk" id="mkG"></div>')(page);
  expect(px[0]).toEqual([0, 0, 255]); expect(px[1]).toEqual([0, 0, 255]);
});

test('R-18 r5b: ホスト CSS が iframe の flex と width/height の両方に !important を付けていても、隣の要素がずれない', async ({ page }) => {
  const px = await flexCase(
    '#flexrow2{position:absolute;left:20px;top:420px;width:300px;display:flex;align-items:flex-start} #flexrow2 iframe{flex:1 1 auto !important;width:100px !important;height:60px !important;border:0} #flexrow2 .mk{flex:1 1 auto;width:40px;height:40px;background:#0000ff}',
    '<iframe srcdoc="x"></iframe><div class="mk" id="mkG"></div>')(page);
  expect(px[0]).toEqual([0, 0, 255]); expect(px[1]).toEqual([0, 0, 255]);
  expect(px[2]).not.toEqual([0, 0, 255]);
});

// ---------- H-M08（2026-10-07）: verify 持ち越しの補強 ----------
test('H-M08: 読み込み済みの画像と、表示中の要素の背景画像は送信画像に描かれる（M6・M16）', async ({ page }) => {
  const url = await greenUrl(page);
  await page.evaluate((u) => {
    const i = document.createElement('img'); i.id = 'gimg'; i.src = u; i.style.cssText = 'position:absolute;left:700px;top:420px;width:40px;height:40px';
    const d = document.createElement('div'); d.id = 'gbg'; d.style.cssText = `position:absolute;left:800px;top:420px;width:40px;height:40px;background-image:url(${u});background-size:cover`;
    document.body.append(i, d);
  }, url);
  await page.waitForFunction(() => document.getElementById('gimg').complete);
  await annotate(page, 30, 90, 300, 200, '描かれる');
  await sendAll(page);
  const px = await pixelsOf(page, [[720, 440], [820, 440]]);
  expect(px[0]).toEqual([0, 255, 0]);
  expect(px[1]).toEqual([0, 255, 0]);
});

test('H-M08: 差し替えできないホストで、iframe が Shadow DOM の中にだけあってもテキストのみにする（P3）', async ({ page }) => {
  const { srv, other } = await pngServer();
  try {
    await page.evaluate((o) => {
      const h = document.createElement('div'); h.style.cssText = 'position:absolute;left:700px;top:420px'; document.body.append(h);
      h.attachShadow({ mode: 'open' }).innerHTML = `<iframe src="${o}/frame.html" width="100" height="60"></iframe>`;
    }, other);
    await page.waitForTimeout(500);
    await page.evaluate(() => Object.freeze(HTMLIFrameElement.prototype));
    const reqs = [];
    page.on('request', (r) => reqs.push(r.url()));
    await annotate(page, 30, 90, 300, 200, '影の iframe');
    await sendAll(page);
    expect(await page.evaluate(() => window.__feedbackTool.lastPayload.png)).toBeNull();
    expect(reqs).toEqual([]);
  } finally { srv.close(); }
});

test('H-M08: html2canvas が同期で例外を投げても、iframe の差し替えは必ず戻る（M20）', async ({ page }) => {
  await page.evaluate(() => { window.html2canvas = () => { throw new Error('sync boom'); }; });
  await annotate(page, 30, 90, 300, 200, '同期例外');
  await sendAll(page);
  expect(await page.evaluate(() => window.__feedbackTool.lastPayload.clipboard.text)).toBe(true);
  expect(await page.evaluate(() => Object.prototype.hasOwnProperty.call(HTMLIFrameElement.prototype, 'cloneNode'))).toBe(false);
});

test('H-M08: 縦並びの flex で伸びる iframe の下の要素が、画像の中でずれない（P8）', async ({ page }) => {
  const px = await columnCase('')(page);
  expect(px[0]).toEqual([0, 0, 255]); expect(px[1]).toEqual([0, 0, 255]); expect(px[2]).not.toEqual([0, 0, 255]);
});

test('H-M08: 縦並びの flex で height と flex に !important があっても、下の要素がずれない（S3）', async ({ page }) => {
  const px = await columnCase('!important')(page);
  expect(px[0]).toEqual([0, 0, 255]); expect(px[1]).toEqual([0, 0, 255]); expect(px[2]).not.toEqual([0, 0, 255]);
});

test('H-M08: 複製した iframe ではインライン onload を実行しない', async ({ page }) => {
  await page.evaluate(() => {
    window.__fl = 0;
    const f = document.createElement('iframe'); f.setAttribute('onload', 'top.__fl++'); f.srcdoc = 'x'; f.style.cssText = 'position:absolute;left:700px;top:420px;width:100px;height:60px';
    document.body.append(f);
  });
  await page.waitForFunction(() => window.__fl === 1);
  await annotate(page, 30, 90, 300, 200, 'onload');
  await sendAll(page);
  await page.waitForTimeout(300);
  expect(await page.evaluate(() => window.__fl)).toBe(1);
});

// Playwright テスト: 取得範囲（R-18: 送信時に取りに行かないもの、壊れた画像、H-M07、H-M19）
import { test, expect } from '@playwright/test';
import { createServer } from 'node:http';
import { useHost, base, sendAll, annotate, greenUrl, pixelsOf, pngServer, noStoreServer } from './helpers.mjs';

useHost(test);

// ---------- verify（2026-10-07）の指摘への対応と補強 ----------
test('R-18: 送信の間、ページの外へのリクエストが出ない（画像の無いページ）', async ({ page }) => {
  const reqs = [];
  page.on('request', (r) => reqs.push(r.url()));
  page.on('websocket', (w) => reqs.push(w.url()));
  await annotate(page, 30, 90, 300, 200, '通信');
  await sendAll(page);
  expect(reqs).toEqual([]);
});

// ---------- r3（2026-10-07 本人決定） ----------
test('R-18 r3: 読み込み済みの画像だけ取り直し、未読み込みの遅延画像は取得しない。WebSocket も出ない', async ({ page }) => {
  // 1x1 PNG を返す別オリジンのサーバ
  const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==', 'base64');
  const img = createServer((req, res) => { res.writeHead(200, { 'content-type': 'image/png', 'access-control-allow-origin': '*' }); res.end(png); });
  await new Promise((r) => img.listen(0, '127.0.0.1', r));
  const other = `http://localhost:${img.address().port}`;
  try {
    await page.evaluate((o) => {
      const a = document.createElement('img'); a.id = 'shown'; a.src = o + '/shown.png'; a.width = 40; a.height = 40;
      const b = document.createElement('img'); b.id = 'lazy'; b.loading = 'lazy'; b.src = o + '/lazy.png'; b.width = 40; b.height = 40; b.style.marginTop = '6000px';
      document.body.prepend(a); document.body.append(b);
    }, other);
    await page.waitForFunction(() => document.getElementById('shown').complete && document.getElementById('shown').naturalWidth > 0);
    expect(await page.evaluate(() => document.getElementById('lazy').complete)).toBe(false);
    const reqs = [];
    page.on('request', (r) => reqs.push(r.method() + ' ' + r.url()));
    page.on('websocket', (w) => reqs.push('WS ' + w.url()));
    await annotate(page, 30, 90, 300, 200, 'ひみつZQX');
    await sendAll(page);
    expect(reqs.filter((u) => u.includes('lazy.png'))).toEqual([]);
    expect(reqs.every((u) => u.startsWith('GET ') && u.includes('/shown.png'))).toBe(true);
    expect(reqs.some((u) => /ZQX|%E3%81%B2/.test(u))).toBe(false);
    // 外した src はホストの DOM に戻っている
    expect(await page.evaluate(() => document.getElementById('lazy').getAttribute('src'))).toBe(other + '/lazy.png');
  } finally {
    img.close();
  }
});

// ---------- r4（2026-10-07 本人決定） ----------
test('R-18 r4: Shadow DOM・picture・srcset の未読み込み画像、非表示要素の背景、iframe の文書を取り直さない', async ({ page }) => {
  const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==', 'base64');
  const srv = createServer((req, res) => {
    if (req.url.endsWith('.html')) { res.writeHead(200, { 'content-type': 'text/html' }); return res.end('<p>other frame</p>'); }
    res.writeHead(200, { 'content-type': 'image/png', 'access-control-allow-origin': '*' }); res.end(png);
  });
  await new Promise((r) => srv.listen(0, '127.0.0.1', r));
  const other = `http://localhost:${srv.address().port}`;
  try {
    await page.evaluate(({ o, b }) => {
      const far = 'margin-top:6000px;display:block';
      const host = document.createElement('div'); document.body.append(host);
      host.attachShadow({ mode: 'open' }).innerHTML = `<img loading="lazy" src="${o}/shadow.png" width="40" height="40" style="${far}">`;
      const pic = document.createElement('picture');
      pic.innerHTML = `<source srcset="${o}/pic-source.png"><img loading="lazy" src="${o}/pic-img.png" width="40" height="40" style="${far}">`;
      document.body.append(pic);
      const ss = document.createElement('img'); ss.loading = 'lazy'; ss.srcset = `${o}/srcset.png 1x`; ss.width = 40; ss.height = 40; ss.style.cssText = far;
      document.body.append(ss);
      const hid = document.createElement('div'); hid.style.cssText = `display:none;background-image:url(${o}/hidden-bg.png)`; document.body.append(hid);
      const f1 = document.createElement('iframe'); f1.id = 'same'; f1.src = b + '/test/plain.html'; f1.style.cssText = 'position:absolute;left:700px;top:420px;width:200px;height:100px'; document.body.append(f1);
      const f2 = document.createElement('iframe'); f2.id = 'cross'; f2.src = o + '/frame.html'; f2.style.cssText = 'position:absolute;left:950px;top:420px;width:200px;height:100px'; document.body.append(f2);
    }, { o: other, b: base });
    await page.waitForFunction(() => document.getElementById('same').contentDocument?.readyState === 'complete');
    await page.waitForTimeout(300);
    const reqs = [];
    page.on('request', (r) => reqs.push(r.method() + ' ' + r.url()));
    page.on('websocket', (w) => reqs.push('WS ' + w.url()));
    await annotate(page, 30, 90, 300, 200, 'ひみつZQX');
    await sendAll(page);
    expect(await page.evaluate(() => !!window.__feedbackTool.lastPayload.png)).toBe(true);
    expect(reqs).toEqual([]);
    // ホスト側の iframe と画像は元のまま
    const host = await page.evaluate(() => ({
      same: document.getElementById('same').contentDocument.body.textContent.includes('ブックマークレット'),
      cross: document.getElementById('cross').getAttribute('src'),
      srcset: document.querySelector('img[srcset]').getAttribute('srcset'),
      source: document.querySelector('picture source').getAttribute('srcset'),
      cloneNodeOwn: Object.prototype.hasOwnProperty.call(HTMLIFrameElement.prototype, 'cloneNode')
    }));
    expect(host.same).toBe(true);
    expect(host.cross).toBe(other + '/frame.html');
    expect(host.srcset).toBe(other + '/srcset.png 1x');
    expect(host.source).toBe(other + '/pic-source.png');
    expect(host.cloneNodeOwn).toBe(false);
  } finally {
    srv.close();
  }
});

// ---------- H-M08（2026-10-07）: verify 持ち越しの補強 ----------
test('H-M08: srcdoc の iframe と、入れ子の Shadow DOM の未読み込み画像も取り直さない（M3・M21）', async ({ page }) => {
  const { srv, other } = await pngServer();
  try {
    await page.evaluate((o) => {
      const f = document.createElement('iframe'); f.id = 'sd'; f.srcdoc = `<img src="${o}/srcdoc.png" width="20" height="20">`;
      f.style.cssText = 'position:absolute;left:700px;top:420px;width:100px;height:60px'; document.body.append(f);
      const h = document.createElement('div'); document.body.append(h);
      const r1 = h.attachShadow({ mode: 'open' }); r1.innerHTML = '<div id="inner"></div>';
      r1.getElementById('inner').attachShadow({ mode: 'open' }).innerHTML = `<img loading="lazy" src="${o}/nested.png" width="40" height="40" style="display:block;margin-top:6000px">`;
    }, other);
    await page.waitForFunction(() => { const d = document.getElementById('sd').contentDocument; return d && d.readyState === 'complete' && d.images[0] && d.images[0].complete; });
    const reqs = [];
    page.on('request', (r) => reqs.push(r.url()));
    await annotate(page, 30, 90, 300, 200, 'srcdoc');
    await sendAll(page);
    expect(reqs).toEqual([]);
  } finally { srv.close(); }
});

test('H-M08: 読み込みに失敗した画像の error イベントをもう一度起こさない', async ({ page }) => {
  const { srv, other } = await pngServer();
  try {
    await page.evaluate((o) => {
      window.__err = 0;
      const i = document.createElement('img'); i.id = 'broken'; i.src = o + '/404.png'; i.addEventListener('error', () => { window.__err++; });
      i.style.cssText = 'position:absolute;left:700px;top:420px;width:40px;height:40px'; document.body.append(i);
    }, other);
    await page.waitForFunction(() => window.__err === 1);
    await annotate(page, 30, 90, 300, 200, '壊れた画像');
    await sendAll(page);
    await page.waitForTimeout(300);
    expect(await page.evaluate(() => window.__err)).toBe(1);
  } finally { srv.close(); }
});

// ---------- r8（2026-10-07 本人決定） ----------
test('R-18 r8: 読み込みに失敗した画像（src・srcset・picture）は送信時に取り直さず、error も再発火しない', async ({ page }) => {
  const { srv, other } = await pngServer();
  try {
    await page.evaluate((o) => {
      window.__err = 0;
      const add = (html) => { const d = document.createElement('div'); d.style.cssText = 'position:absolute;left:700px;top:420px'; d.innerHTML = html; document.body.append(d); return d; };
      add(`<img id="b1" src="${o}/404-a.png" width="40" height="40">`);
      add(`<img id="b2" srcset="${o}/404-b.png 1x" width="40" height="40">`);
      add(`<picture><source srcset="${o}/404-c.png"><img id="b3" src="${o}/404-d.png" width="40" height="40"></picture>`);
      document.querySelectorAll('#b1,#b2,#b3').forEach((i) => i.addEventListener('error', () => { window.__err++; }));
    }, other);
    await page.waitForFunction(() => ['b1', 'b2', 'b3'].every((id) => document.getElementById(id).complete));
    await page.waitForTimeout(200);
    const errBefore = await page.evaluate(() => window.__err);
    const reqs = [];
    page.on('request', (r) => reqs.push(r.url()));
    await annotate(page, 30, 90, 300, 200, '壊れた画像');
    await sendAll(page);
    await page.waitForTimeout(300);
    expect(await page.evaluate(() => !!window.__feedbackTool.lastPayload.png)).toBe(true);
    expect(reqs).toEqual([]);
    expect(await page.evaluate(() => window.__err)).toBe(errBefore);
    // ホストの画像は元のまま
    expect(await page.evaluate(() => [document.getElementById('b1').getAttribute('src'), document.getElementById('b2').getAttribute('srcset')]))
      .toEqual([other + '/404-a.png', other + '/404-b.png 1x']);
    expect(await page.evaluate(() => Object.prototype.hasOwnProperty.call(HTMLImageElement.prototype, 'cloneNode'))).toBe(false);
  } finally { srv.close(); }
});

test('R-18 r8: 差し替えできないホストで壊れた画像があれば、画像を作らずテキストのみ送る', async ({ page }) => {
  const { srv, other } = await pngServer();
  try {
    await page.evaluate((o) => {
      const i = document.createElement('img'); i.id = 'bx'; i.src = o + '/404-x.png'; i.style.cssText = 'position:absolute;left:700px;top:420px;width:40px;height:40px';
      document.body.append(i);
    }, other);
    await page.waitForFunction(() => document.getElementById('bx').complete);
    await page.evaluate(() => Object.freeze(HTMLImageElement.prototype));
    const reqs = [];
    page.on('request', (r) => reqs.push(r.url()));
    await annotate(page, 30, 90, 300, 200, '凍結');
    await sendAll(page);
    expect(await page.evaluate(() => window.__feedbackTool.lastPayload.png)).toBeNull();
    expect(reqs).toEqual([]);
  } finally { srv.close(); }
});

test('R-18 r8b: HTMLSourceElement だけ凍結されていても、差し替えを残さずテキストのみ送る（verify-r8 N1）', async ({ page }) => {
  const { srv, other } = await pngServer();
  try {
    await page.evaluate((o) => {
      const i = document.createElement('img'); i.id = 'by'; i.src = o + '/404-y.png'; i.style.cssText = 'position:absolute;left:700px;top:420px;width:40px;height:40px';
      document.body.append(i);
    }, other);
    await page.waitForFunction(() => document.getElementById('by').complete);
    await page.evaluate(() => Object.freeze(HTMLSourceElement.prototype));
    const reqs = [];
    page.on('request', (r) => reqs.push(r.url()));
    await annotate(page, 30, 90, 300, 200, '片方凍結');
    await sendAll(page);
    expect(await page.evaluate(() => window.__feedbackTool.lastPayload.png)).toBeNull();
    expect(reqs).toEqual([]);
    expect(await page.evaluate(() => [HTMLImageElement.prototype, HTMLSourceElement.prototype].map((P) => Object.prototype.hasOwnProperty.call(P, 'cloneNode')))).toEqual([false, false]);
  } finally { srv.close(); }
});

// ---------- H-M07（2026-10-07）: R-18 の列挙外（ページが読み込んでいないもの）を取りに行かない ----------
test('H-M07: 見えない要素（閉じた details・until-found・content-visibility・display:none）の背景・リスト画像・疑似要素の画像を取りに行かない', async ({ page }) => {
  const { srv, other } = await noStoreServer();
  try {
    await page.evaluate((o) => {
      const d = document.createElement('div'); d.id = 'h07';
      d.innerHTML = `
        <details><summary>開く</summary><div style="width:40px;height:40px;background-image:url(${o}/details-bg.png)"></div></details>
        <div hidden="until-found"><div style="width:40px;height:40px;background-image:url(${o}/until-bg.png)"></div></div>
        <div style="content-visibility:hidden;width:100px;height:50px"><div style="width:40px;height:40px;background-image:url(${o}/cv-bg.png)"></div></div>
        <ul style="display:none;list-style-image:url(${o}/list.png)"><li>項目</li></ul>
        <style>#h07 .ps::before{content:url(${o}/pseudo.png)} #h07 .ps2 i::after{content:url(${o}/pseudo2.png)}</style>
        <div class="ps" style="display:none"></div>
        <div class="ps2" style="display:none"><i>子</i></div>`;
      document.body.append(d);
    }, other);
    await page.waitForTimeout(300);
    const reqs = [];
    page.on('request', (r) => reqs.push(r.url()));
    await annotate(page, 30, 90, 300, 200, '見えない要素');
    await sendAll(page);
    expect(await page.evaluate(() => window.__feedbackTool.lastPayload.captureError)).toBeNull();
    expect(await page.evaluate(() => !!window.__feedbackTool.lastPayload.png)).toBe(true);
    expect(reqs).toEqual([]);
    // ホストに目印や style を残さない
    expect(await page.evaluate(() => document.querySelectorAll('[data-fb-hidden], style[data-fb-pseudo]').length)).toBe(0);
  } finally { srv.close(); }
});

test('H-M07: 見えている要素の疑似要素の画像は、従来どおり描かれる', async ({ page }) => {
  const url = await greenUrl(page);
  await page.evaluate((u) => {
    const st = document.createElement('style'); st.textContent = `#vis::before{content:"";display:block;width:40px;height:40px;background-image:url(${u});background-size:cover}`;
    document.head.append(st);
    const d = document.createElement('div'); d.id = 'vis'; d.style.cssText = 'position:absolute;left:700px;top:420px;width:40px;height:40px';
    document.body.append(d);
  }, url);
  await page.waitForTimeout(200);
  await annotate(page, 30, 90, 300, 200, '見える疑似要素');
  await sendAll(page);
  const px = await pixelsOf(page, [[720, 440]]);
  expect(px[0]).toEqual([0, 255, 0]);
});

test('H-M07: audio（preload=none・source 子要素）と prefetch の link を、送信時に取りに行かない', async ({ page }) => {
  const { srv, other } = await noStoreServer();
  try {
    await page.evaluate((o) => {
      const a1 = document.createElement('audio'); a1.preload = 'none'; a1.src = o + '/a1.mp3'; a1.controls = true; document.body.append(a1);
      const a2 = document.createElement('audio'); a2.preload = 'none'; a2.innerHTML = `<source src="${o}/a2.mp3" type="audio/mpeg">`; document.body.append(a2);
      const l = document.createElement('link'); l.rel = 'prefetch'; l.href = o + '/pre.png'; document.head.append(l);
    }, other);
    await page.waitForTimeout(800);   // prefetch はページ自身が先に取りに行く（no-store なので複製すると取り直しになる）
    const reqs = [];
    page.on('request', (r) => reqs.push(r.url()));
    await annotate(page, 30, 90, 300, 200, 'audio');
    await sendAll(page);
    expect(await page.evaluate(() => !!window.__feedbackTool.lastPayload.png)).toBe(true);
    expect(reqs).toEqual([]);
    expect(await page.evaluate(() => ['HTMLAudioElement', 'HTMLLinkElement'].map((n) => Object.prototype.hasOwnProperty.call(window[n].prototype, 'cloneNode')))).toEqual([false, false]);
  } finally { srv.close(); }
});

// ---------- verify-h-m07 の差し戻し対応 ----------
test('H-M07b N1・N2: 画面内の content-visibility:auto の節の背景と、display:contents の疑似要素は送信画像に描かれる', async ({ page }) => {
  const url = await greenUrl(page);
  await page.evaluate((u) => {
    const st = document.createElement('style');
    st.textContent = `#dc::before{content:"";display:inline-block;width:40px;height:40px;background-image:url(${u});background-size:cover}`;
    document.head.append(st);
    const a = document.createElement('section'); a.style.cssText = 'content-visibility:auto;contain-intrinsic-size:auto 60px;position:absolute;left:700px;top:420px;width:60px;height:60px';
    a.innerHTML = `<div style="width:40px;height:40px;background-image:url(${u});background-size:cover"></div>`;
    const w = document.createElement('div'); w.style.cssText = 'position:absolute;left:800px;top:420px';
    w.innerHTML = '<span id="dc" style="display:contents"></span>';
    document.body.append(a, w);
  }, url);
  await page.waitForTimeout(300);
  await annotate(page, 30, 90, 300, 200, '見えている');
  await sendAll(page);
  const px = await pixelsOf(page, [[720, 440], [820, 440]]);
  expect(px[0]).toEqual([0, 255, 0]);
  expect(px[1]).toEqual([0, 255, 0]);
});

test('H-M07b N3: 既定の preload の audio、Shadow DOM の中の閉じた details、画面外の content-visibility:auto の背景を取りに行かない', async ({ page }) => {
  const { srv, other } = await noStoreServer();
  try {
    await page.evaluate((o) => {
      const a = document.createElement('audio'); a.src = o + '/a3.mp3'; document.body.append(a);   // preload 既定（ページ自身が先に読み込む）
      const h = document.createElement('div'); document.body.append(h);
      h.attachShadow({ mode: 'open' }).innerHTML = `<details><summary>s</summary><div style="width:40px;height:40px;background-image:url(${o}/shadow-details.png)"></div></details>`;
      const sp = document.createElement('div'); sp.style.height = '4000px'; document.body.append(sp);
      const cv = document.createElement('section'); cv.style.cssText = 'content-visibility:auto;contain-intrinsic-size:auto 100px';
      cv.innerHTML = `<div style="width:40px;height:40px;background-image:url(${o}/cv-auto.png)"></div>`;
      document.body.append(cv);
    }, other);
    await page.waitForTimeout(800);
    const reqs = [];
    page.on('request', (r) => reqs.push(r.url()));
    await annotate(page, 30, 90, 300, 200, 'N3');
    await sendAll(page);
    expect(await page.evaluate(() => !!window.__feedbackTool.lastPayload.png)).toBe(true);
    expect(reqs).toEqual([]);
  } finally { srv.close(); }
});

test('H-M07c N9: 見えない部分木の奥（閉じた details の孫・Shadow DOM 直下の閉じた details）でも、判定のために画像を読み込ませない', async ({ page }) => {
  const { srv, other } = await noStoreServer();
  try {
    await page.evaluate((o) => {
      const d = document.createElement('div');
      d.innerHTML = `<details><summary>s</summary><div><div><div style="width:40px;height:40px;background-image:url(${o}/deep1.png)"></div></div></div></details>
        <div hidden="until-found"><section><p style="background-image:url(${o}/deep2.png)">x</p></section></div>`;
      document.body.append(d);
      const h = document.createElement('div'); document.body.append(h);
      h.attachShadow({ mode: 'open' }).innerHTML = `<details><summary>s</summary><span style="display:block;width:20px;height:20px;background-image:url(${o}/deep3.png)"></span></details>`;
    }, other);
    await page.waitForTimeout(300);
    const reqs = [];
    page.on('request', (r) => reqs.push(r.url()));
    await annotate(page, 30, 90, 300, 200, '奥');
    await sendAll(page);
    expect(reqs).toEqual([]);
  } finally { srv.close(); }
});

// ---------- H-M19（2026-10-08）: N8 display:contents の入れ子・content-visibility:auto の直下 ----------
test('H-M19 N8: display:contents の入れ子と、画面内の content-visibility:auto の節の直下にある疑似要素は送信画像に描かれる', async ({ page }) => {
  const url = await greenUrl(page);
  await page.evaluate((u) => {
    const st = document.createElement('style');
    st.textContent = `.pz::before{content:"";display:inline-block;width:40px;height:40px;background-image:url(${u});background-size:cover}`;
    document.head.append(st);
    const a = document.createElement('div'); a.style.cssText = 'position:absolute;left:700px;top:420px';
    a.innerHTML = '<span style="display:contents"><span class="pz" style="display:contents"></span></span>';
    const b = document.createElement('section'); b.style.cssText = 'content-visibility:auto;contain-intrinsic-size:auto 60px;position:absolute;left:800px;top:420px;width:60px;height:60px';
    b.innerHTML = '<span class="pz" style="display:contents"></span>';
    document.body.append(a, b);
  }, url);
  await page.waitForTimeout(300);
  await annotate(page, 30, 90, 300, 200, '入れ子');
  await sendAll(page);
  const px = await pixelsOf(page, [[720, 440], [820, 440]]);
  expect(px[0]).toEqual([0, 255, 0]);
  expect(px[1]).toEqual([0, 255, 0]);
});

test('H-M19 N8: 画面外の content-visibility:auto の節の中の display:contents の疑似要素の画像は、取りに行かない', async ({ page }) => {
  const { srv, other } = await noStoreServer();
  try {
    await page.evaluate((o) => {
      const st = document.createElement('style');
      st.textContent = `.pq::before{content:url(${o}/cv-pseudo.png)} .pq2::before{content:url(${o}/dc-none.png)}`;
      document.head.append(st);
      const sp = document.createElement('div'); sp.style.height = '4000px'; document.body.append(sp);
      const sec = document.createElement('section'); sec.style.cssText = 'content-visibility:auto;contain-intrinsic-size:auto 100px';
      sec.innerHTML = '<span class="pq" style="display:contents"></span>';
      document.body.append(sec);
      const n = document.createElement('div'); n.style.display = 'none';
      n.innerHTML = '<span style="display:contents"><span class="pq2" style="display:contents"></span></span>';
      document.body.append(n);
    }, other);
    await page.waitForTimeout(300);
    const reqs = [];
    page.on('request', (r) => reqs.push(r.url()));
    await annotate(page, 30, 90, 300, 200, '画面外');
    await sendAll(page);
    expect(reqs).toEqual([]);
  } finally { srv.close(); }
});

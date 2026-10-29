// Playwright テスト: 性能（R-17）と画面外の要素の間引き（H-M18）
import { test, expect } from '@playwright/test';
import { useHost, sendAll, annotate, greenUrl, pixelsOf, noStoreServer, sameAsNoCull } from './helpers.mjs';

useHost(test);

// ---------- verify（2026-10-07）の指摘への対応と補強 ----------
test('R-17: 1920x1080・注釈3件で送信が 2 秒以内に終わる', async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 });
  await annotate(page, 30, 90, 300, 200, '速さ1');
  await annotate(page, 400, 90, 700, 200, '速さ2');
  await annotate(page, 30, 300, 700, 500, '速さ3');
  const t0 = Date.now();
  await sendAll(page);
  expect(Date.now() - t0).toBeLessThan(2000);
});

// ---------- H-M18（2026-10-08） ----------
test('H-M18 N6: 2万要素のページでも 1920x1080 で送信が 2 秒以内（画面外の要素は描画対象から外す）', async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 });
  await page.evaluate(() => {
    const box = document.createElement('div');
    box.innerHTML = Array.from({ length: 5000 }, (_, i) => `<div class="r"><span>項目 ${i}</span><b>x</b><i>y</i></div>`).join('');
    document.body.append(box);
  });
  await annotate(page, 30, 90, 300, 200, '重い');
  const t0 = Date.now();
  await sendAll(page);
  expect(Date.now() - t0).toBeLessThan(2000);
  expect(await page.evaluate(() => !!window.__feedbackTool.lastPayload.png)).toBe(true);
});

test('H-M18 N6: 画面外の要素を外しても、画面に写るもの（固定・絶対位置の子・はみ出す中身・スクロール後の内容）は描かれる', async ({ page }) => {
  const url = await greenUrl(page);
  await page.evaluate((u) => {
    const g = `background-image:url(${u});background-size:cover`;
    document.body.insertAdjacentHTML('beforeend', `
      <div style="height:3000px"></div>
      <div id="fixed-parent" style="height:20px"><div style="position:fixed;left:1100px;top:420px;width:40px;height:40px;${g}"></div></div>
      <div id="abs-parent" style="position:relative;height:20px">
        <div style="position:absolute;top:900px;left:700px;width:40px;height:40px;${g}"></div>
      </div>
      <div style="height:10px;overflow:visible"><div style="height:1100px"></div><div style="width:40px;height:40px;margin-left:800px;${g}"></div></div>
      <div style="height:700px"></div>
      <div style="width:40px;height:40px;margin-left:900px;${g}" id="inflow"></div>
      <div style="height:3000px"></div>
      <div style="position:fixed;left:1000px;top:420px;width:40px;height:40px;${g}"></div>`);
  }, url);
  // abs-parent が画面の上に外れるまでスクロール
  const y = await page.evaluate(() => document.getElementById('abs-parent').getBoundingClientRect().top + window.scrollY + 400);
  await page.evaluate((y) => window.scrollTo(0, y), y);
  await page.waitForTimeout(200);
  const pts = await page.evaluate(() => {
    const out = [];
    const ab = document.querySelector('#abs-parent > div').getBoundingClientRect(); out.push([ab.x + 20, ab.y + 20]);
    const ov = document.querySelector('div[style*="overflow:visible"] > div:last-child').getBoundingClientRect(); out.push([ov.x + 20, ov.y + 20]);
    const inf = document.getElementById('inflow').getBoundingClientRect(); out.push([inf.x + 20, inf.y + 20]);
    out.push([1020, 440]);
    out.push([1120, 440]);   // 画面外の親の中の fixed（親のはみ出しには数えられない）
    return out;
  });
  for (const [x, yy] of pts) { expect(x).toBeGreaterThan(0); expect(yy).toBeGreaterThan(0); expect(yy).toBeLessThan(800); }
  await annotate(page, 30, 90, 300, 200, '写るもの');
  await sendAll(page);
  const px = await pixelsOf(page, pts);
  for (const c of px) expect(c).toEqual([0, 255, 0]);
});

test('H-M18 N4: 見えない要素の目印は部分木の頂点だけに付け、ホストの MutationObserver に要素数ぶんの記録を送らない', async ({ page }) => {
  await page.evaluate(() => {
    const d = document.createElement('div'); d.style.display = 'none';
    d.innerHTML = Array.from({ length: 500 }, () => '<div><span>x</span></div>').join('');
    document.body.append(d);
    const det = document.createElement('details'); det.innerHTML = '<summary>s</summary>' + Array.from({ length: 200 }, () => '<p><b>y</b></p>').join('');
    document.body.append(det);
    window.__mut = 0;
    new MutationObserver((ms) => { window.__mut += ms.filter((m) => m.type === 'attributes' && m.attributeName === 'data-fb-hidden').length; })
      .observe(document.body, { attributes: true, subtree: true });
  });
  await annotate(page, 30, 90, 300, 200, '記録');
  await sendAll(page);
  await page.waitForTimeout(100);
  // 目印は頂点だけ: display:none の div 1つ＋閉じた details の直下 200 → 付けて外すで約 402 件（以前は全要素 1700 で約 3400 件）
  expect(await page.evaluate(() => window.__mut)).toBeLessThan(420);
});

test('H-M18 N5: ホストが Element.prototype に入れた cloneNode を、audio・link の複製でも通る', async ({ page }) => {
  await page.evaluate(() => {
    const a = document.createElement('audio'); a.preload = 'none'; a.src = '/nothing.mp3'; document.body.append(a);
    const l = document.createElement('link'); l.rel = 'prefetch'; l.href = '/nothing.png'; document.head.append(l);
    window.__cc = 0;
    const orig = Node.prototype.cloneNode;
    Element.prototype.cloneNode = function (deep) { if (this.tagName === 'AUDIO' || this.tagName === 'LINK') window.__cc++; return orig.call(this, deep); };
  });
  await annotate(page, 30, 90, 300, 200, 'ホストの cloneNode');
  await sendAll(page);
  expect(await page.evaluate(() => window.__cc)).toBeGreaterThanOrEqual(2);
  expect(await page.evaluate(() => !!window.__feedbackTool.lastPayload.png)).toBe(true);
  await page.evaluate(() => { delete Element.prototype.cloneNode; });
});

test('H-M18 N20: Shadow DOM 直下の入れ子の display:contents の疑似要素は描かれる', async ({ page }) => {
  const url = await greenUrl(page);
  await page.evaluate((u) => {
    const h = document.createElement('div'); h.style.cssText = 'position:absolute;left:700px;top:420px';
    document.body.append(h);
    h.attachShadow({ mode: 'open' }).innerHTML = `<style>.pz::before{content:"";display:inline-block;width:40px;height:40px;background-image:url(${u});background-size:cover}</style>`
      + '<span style="display:contents"><span class="pz" style="display:contents"></span></span>';
  }, url);
  await page.waitForTimeout(200);
  await annotate(page, 30, 90, 300, 200, '影');
  await sendAll(page);
  const px = await pixelsOf(page, [[720, 440]]);
  expect(px[0]).toEqual([0, 255, 0]);
});

// ---------- verify-h-m18 の差し戻し対応 ----------
test('H-M18b F1: 画面の下にある親から、上へ動いて画面に入ってくるもの（translate・relative・column-reverse）は描かれる', async ({ page }) => {
  const url = await greenUrl(page);
  await page.evaluate((u) => {
    const g = `background-image:url(${u});background-size:cover`;
    document.body.insertAdjacentHTML('beforeend', `
      <div style="height:3000px"></div>
      <div id="anchor" style="height:20px"></div>
      <div style="height:20px"><div style="width:40px;height:40px;translate:600px -520px;${g}"></div></div>
      <div style="height:20px"><div style="width:40px;height:40px;position:relative;left:700px;top:-520px;${g}"></div></div>
      <div style="height:20px;display:flex;flex-direction:column-reverse"><div style="height:500px;flex-shrink:0"></div><div style="width:40px;height:40px;flex-shrink:0;margin-left:800px;${g}"></div></div>
      <div style="height:3000px"></div>`);
  }, url);
  // 親たちが画面の下に外れる位置（画面下端の 300px 下）へスクロール
  const y = await page.evaluate(() => document.getElementById('anchor').getBoundingClientRect().top + window.scrollY - window.innerHeight - 300);
  await page.evaluate((y) => window.scrollTo(0, y), y);
  await page.waitForTimeout(200);
  const pts = await page.evaluate(() => Array.from(document.querySelectorAll('[style*="translate:600px"], [style*="left:700px"], [style*="margin-left:800px"]'))
    .map((e) => { const r = e.getBoundingClientRect(); return [Math.round(r.x + 20), Math.round(r.y + 20)]; }));
  expect(pts.length).toBe(3);
  for (const [x, yy] of pts) { expect(yy).toBeGreaterThan(0); expect(yy).toBeLessThan(800); }
  await annotate(page, 30, 90, 300, 200, '上へ動く');
  await sendAll(page);
  const px = await pixelsOf(page, pts);
  for (const c of px) expect(c).toEqual([0, 255, 0]);
});

test('H-M18b N21: 閉じた details の直下の Shadow DOM の中の背景は、取りに行かない（Shadow DOM の境界）', async ({ page }) => {
  const { srv, other } = await noStoreServer();
  try {
    await page.evaluate((o) => {
      const det = document.createElement('details'); det.innerHTML = '<summary>s</summary><div id="sh21"></div>';
      document.body.append(det);
      det.querySelector('#sh21').attachShadow({ mode: 'open' }).innerHTML = `<div style="width:40px;height:40px;background-image:url(${o}/sh21.png)"></div>`;
    }, other);
    await page.waitForTimeout(300);
    const reqs = [];
    page.on('request', (r) => reqs.push(r.url()));
    await annotate(page, 30, 90, 300, 200, '影の境界');
    await sendAll(page);
    expect(reqs).toEqual([]);
  } finally { srv.close(); }
});

// ---------- verify-h-m18b の N24・N23 ----------
test('H-M18c N24: よくある書き方（影付きカード・ellipsis・hidden input・display:none の子・body の text-shadow）の2万要素でも、間引きが効いて 3 秒以内（間引かないと 4〜9 秒）', async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 });
  await page.evaluate(() => {
    document.body.style.textShadow = '0 1px 0 rgba(0,0,0,.1)';
    const box = document.createElement('div');
    box.innerHTML = Array.from({ length: 4000 }, (_, i) =>
      `<div style="box-shadow:0 1px 3px rgba(0,0,0,.2);margin:4px;padding:4px"><span style="display:block;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;width:200px">行 ${i} のとても長い説明文がここに入ります</span><input type="hidden" value="${i}"><b style="display:none">md</b></div>`).join('');
    document.body.append(box);
  });
  await annotate(page, 30, 90, 300, 200, 'よくある書き方');
  const t0 = Date.now();
  await sendAll(page);
  expect(Date.now() - t0).toBeLessThan(3000);
});

test('H-M18c N23: 画面外の要素の影・はみ出す文字が画面にかかるなら、間引いても間引かないときと同じに描かれる', async ({ page }) => {
  await page.evaluate(() => {
    document.body.insertAdjacentHTML('beforeend', `
      <div style="position:absolute;left:3000px;top:200px;width:10px;height:10px;box-shadow:0 0 0 9999px rgb(0,255,0)"></div>
      <div id="ovf" style="position:absolute;left:-900px;top:700px;width:100px;white-space:nowrap;font:40px/40px monospace;color:rgb(0,0,255)">${'█'.repeat(60)}</div>`);
  });
  await page.waitForTimeout(200);
  await annotate(page, 30, 90, 300, 200, '影とはみ出し');
  const pts = [[1250, 30], [100, 720]];
  const shoot = async (noCull) => {
    await page.evaluate((v) => { window.__fbNoCull = v; window.__feedbackTool.clear(); }, noCull);
    await annotate(page, 30, 90, 300, 200, 'x');
    await sendAll(page);
    return pixelsOf(page, pts);
  };
  const ref = await shoot(true);
  const got = await shoot(false);
  for (const c of ref) expect(c).not.toEqual([255, 255, 255]);   // 間引きなしでは何か描かれている
  expect(got).toEqual(ref);
});

// ---------- verify-h-m18c の F1・N27・N28・N23 ----------
test('H-M18d: display:contents 直下の文字・RTL の左へのはみ出し・拡大したはみ出し・text-shadow・下へのはみ出しは、間引いても同じに描かれる', async ({ page }) => {
  await page.evaluate(() => {
    const W = window.innerWidth;
    document.body.insertAdjacentHTML('beforeend', `
      <div style="position:absolute;left:400px;top:300px;font:40px/40px monospace;color:rgb(0,0,255)"><span style="display:contents">${'█'.repeat(5)}<a style="position:relative;top:3000px">link</a></span></div>
      <div dir="rtl" style="position:absolute;left:${W + 300}px;top:380px;width:100px;white-space:nowrap;font:40px/40px monospace;color:rgb(0,0,255)">${'█'.repeat(60)}</div>
      <div style="position:absolute;left:-900px;top:460px;width:100px;white-space:nowrap;font:20px/20px monospace;color:rgb(0,0,255);transform:scale(3);transform-origin:0 0">${'█'.repeat(35)}</div>
      <div style="position:absolute;left:600px;top:-600px;font:40px/40px monospace;color:rgb(0,0,255);text-shadow:0 1000px 0 rgb(0,0,255)">██</div>
      <div style="position:absolute;left:800px;top:-700px;width:100px;height:10px;white-space:pre;font:40px/40px monospace;color:rgb(0,0,255)">${'\n'.repeat(18)}██</div>`);
  });
  await page.waitForTimeout(200);
  const pts = [[440, 320], [600, 400], [150, 490], [620, 420], [820, 30]];
  const { ref, got } = await sameAsNoCull(page, pts);
  for (const c of ref) expect(c).not.toEqual([255, 255, 255]);   // 間引きなしでは何か描かれている
  expect(got).toEqual(ref);
});

// ---------- H-M20（2026-10-08）: 縦書き・回転・縦の倍率・display:contents の影・負の text-indent ----------
test('H-M20: 縦書き・回転・縦の倍率・display:contents の文字の影・負の text-indent で画面に入るものは、間引いても同じに描かれる', async ({ page }) => {
  await page.evaluate(() => {
    const W = window.innerWidth, B = 'font:40px/40px monospace;color:rgb(0,0,255)';
    document.body.insertAdjacentHTML('beforeend', `
      <div style="position:absolute;left:${W + 300}px;top:100px;width:40px;height:100px;writing-mode:vertical-rl;word-break:break-all;${B}">${'█'.repeat(60)}</div>
      <div style="position:absolute;left:${W + 300}px;top:200px;width:100px;white-space:nowrap;transform:rotate(180deg);${B}">${'█'.repeat(60)}</div>
      <div style="position:absolute;left:800px;top:-400px;width:100px;height:10px;white-space:pre;transform:scaleY(3);transform-origin:0 0;${B}">${'\n'.repeat(3)}██</div>
      <div style="position:absolute;left:400px;top:-600px;${B}"><span style="display:contents;text-shadow:0 1000px 0 rgb(0,0,255)">██</span></div>
      <div style="position:absolute;left:${W + 300}px;top:500px;width:100px;white-space:nowrap;text-indent:-1000px;${B}">██</div>`);
  });
  await page.waitForTimeout(200);
  const pts = [[1200, 150], [900, 220], [820, 40], [420, 420], [600, 520]];
  const { ref, got } = await sameAsNoCull(page, pts);
  for (const c of ref) expect(c).not.toEqual([255, 255, 255]);   // 間引きなしでは何か描かれている
  expect(got).toEqual(ref);
});

// ---------- H-M24（2026-10-09） ----------
test('H-M24 N33: 右へ大きくはみ出す行が画面の下に並んでいても、それらは間引かれる（はみ出しは書く向きにだけ広げる）', async ({ page }) => {
  await page.evaluate(() => {
    const rows = Array.from({ length: 200 }, (_, i) =>
      `<div class="ovr" style="position:absolute;left:0;top:${1100 + i * 20}px;width:100px;white-space:nowrap;font:20px/20px monospace">${'x'.repeat(60)}</div>`).join('');
    document.body.insertAdjacentHTML('beforeend', rows);
  });
  await annotate(page, 30, 90, 300, 200, '下の行');
  await sendAll(page);
  expect(await page.evaluate(() => window.__feedbackTool.lastPayload.culled)).toBeGreaterThanOrEqual(200);
});

test('H-M24 N34・N35: flex の終わり寄せで上へ出る文字・row-reverse で左へ出る文字・横だけの拡大・回転で上へ出る文字は、間引いても同じに描かれる', async ({ page }) => {
  await page.evaluate(() => {
    const W = window.innerWidth, H = window.innerHeight, B = 'font:40px/40px monospace;color:rgb(0,0,255)';
    document.body.insertAdjacentHTML('beforeend', `
      <div style="position:absolute;left:1000px;top:${H + 300}px;width:100px;height:20px;display:flex;flex-direction:column;justify-content:flex-end;white-space:pre;${B}">${'██\n'.repeat(20)}██</div>
      <div style="position:absolute;left:${W + 300}px;top:500px;width:100px;display:flex;flex-direction:row-reverse;white-space:nowrap;${B}">${'█'.repeat(60)}</div>
      <div style="position:absolute;left:-900px;top:580px;width:100px;white-space:nowrap;font:20px/20px monospace;color:rgb(0,0,255);transform:scaleX(3);transform-origin:0 0">${'█'.repeat(35)}</div>
      <div style="position:absolute;left:600px;top:${H + 400}px;width:100px;white-space:nowrap;transform:rotate(-90deg);transform-origin:0 0;${B}">${'█'.repeat(60)}</div>`);
  });
  await page.waitForTimeout(200);
  const pts = [[1020, 400], [800, 520], [150, 590], [620, 250]];
  const { ref, got } = await sameAsNoCull(page, pts);
  for (const c of ref) expect(c).not.toEqual([255, 255, 255]);   // 間引きなしでは何か描かれている
  expect(got).toEqual(ref);
});

// ---------- verify-h-m24 の差し戻し対応（F1・F2・N39） ----------
test('H-M24b: 祖先の回転・個別の rotate・grid の寄せ・sideways の文字は、間引いても同じに描かれる', async ({ page }) => {
  await page.evaluate(() => {
    const W = window.innerWidth, H = window.innerHeight, B = 'font:40px/40px monospace;color:rgb(0,0,255)';
    document.body.insertAdjacentHTML('beforeend', `
      <div style="position:absolute;left:${W + 600}px;top:100px;width:100px;transform:rotate(180deg)"><div id="c1" style="width:100px;white-space:nowrap;${B}">${'█'.repeat(60)}</div></div>
      <div id="c2" style="position:absolute;left:${W + 600}px;top:200px;width:100px;white-space:nowrap;rotate:180deg;${B}">${'█'.repeat(60)}</div>
      <div id="c4" style="position:absolute;left:900px;top:${H + 600}px;width:100px;height:20px;display:grid;align-content:end;white-space:pre;${B}">${'██\n'.repeat(30)}██</div>
      <div id="c5" style="position:absolute;left:300px;top:-1000px;height:100px;width:40px;writing-mode:sideways-lr;direction:rtl;white-space:nowrap;${B}">${'█'.repeat(60)}</div>`);
  });
  await page.waitForTimeout(200);
  // 確認する点は、実際に文字が描かれている所（ビューポートと重なる行の中心）から選ぶ
  const pts = await page.evaluate(() => ['c1', 'c2', 'c4', 'c5'].map((id) => {
    const W = window.innerWidth, H = window.innerHeight, rg = document.createRange();
    rg.selectNodeContents(document.getElementById(id));
    for (const r of rg.getClientRects()) {
      const l = Math.max(r.left, 0), t = Math.max(r.top, 0), rr = Math.min(r.right, W), b = Math.min(r.bottom, H);
      if (rr - l > 10 && b - t > 10) return [Math.round((l + rr) / 2), Math.round((t + b) / 2)];
    }
    return null;
  }));
  expect(pts.every(Boolean)).toBe(true);   // どの例も画面にかかっている
  const { ref, got } = await sameAsNoCull(page, pts);
  for (const c of ref) expect(c).not.toEqual([255, 255, 255]);
  expect(got).toEqual(ref);
});

// ---------- H-M25（2026-10-09） ----------
const visiblePoints = (page, ids) => page.evaluate((ids) => ids.map((id) => {
  const W = window.innerWidth, H = window.innerHeight, rg = document.createRange();
  rg.selectNodeContents(document.getElementById(id));
  for (const r of rg.getClientRects()) {
    const l = Math.max(r.left, 0), t = Math.max(r.top, 0), rr = Math.min(r.right, W), b = Math.min(r.bottom, H);
    if (rr - l > 10 && b - t > 10) return [Math.round((l + rr) / 2), Math.round((t + b) / 2)];
  }
  return null;
}), ids);

for (const tf of ['translateZ(0)', 'translate3d(0,0,1px)']) test(`H-M25 N41・N42: ${tf} の箱でページ全体を包んでも、画面の下の右へはみ出す行は間引かれる`, async ({ page }) => {
  await page.evaluate((tf) => {
    const rows = Array.from({ length: 200 }, (_, i) =>
      `<div class="ovr" style="position:absolute;left:0;top:${1100 + i * 20}px;width:100px;white-space:nowrap;font:20px/20px monospace">${'x'.repeat(60)}</div>`).join('');
    document.body.insertAdjacentHTML('beforeend', `<div id="wrap" style="position:absolute;left:0;top:0;transform:${tf}">${rows}</div>`);
  }, tf);
  await annotate(page, 30, 90, 300, 200, '包む');
  await sendAll(page);
  expect(await page.evaluate(() => window.__feedbackTool.lastPayload.culled)).toBeGreaterThanOrEqual(200);
});

test('H-M25: 2段上の祖先の回転・60°の回転・scale3d の反転・sideways-lr の上・負の個別 scale の親・上へのはみ出し（回転・scaleY(-1)・offset-path）・sideways-rl・plaintext・縦書きの上・-webkit-box は、間引いても同じに描かれる', async ({ page }) => {
  await page.evaluate(() => {
    const W = window.innerWidth, H = window.innerHeight, B = 'font:40px/40px monospace;color:rgb(0,0,255)', R = '█'.repeat(60);
    document.body.insertAdjacentHTML('beforeend', `
      <div style="position:absolute;left:${W + 600}px;top:60px;width:100px;transform:rotate(180deg)"><div><div id="g1" style="width:100px;white-space:nowrap;${B}">${R}</div></div></div>
      <div style="position:absolute;left:${W + 600}px;top:120px;width:100px;scale:-1 1"><div id="g2" style="width:100px;white-space:nowrap;${B}">${R}</div></div>
      <div id="g3" style="position:absolute;left:200px;top:${H + 400}px;width:100px;white-space:nowrap;transform:rotate(270deg);transform-origin:0 0;${B}">${R}</div>
      <div id="g4" style="position:absolute;left:${W + 300}px;top:180px;height:100px;width:40px;writing-mode:sideways-rl;word-break:break-all;${B}">${R}</div>
      <div id="g5" style="position:absolute;left:${W + 300}px;top:300px;width:100px;white-space:nowrap;unicode-bidi:plaintext;${B}">א${R}</div>
      <div id="g6" style="position:absolute;left:700px;top:${H + 300}px;height:100px;width:40px;writing-mode:vertical-lr;direction:rtl;white-space:nowrap;${B}">${R}</div>
      <div id="g7" style="position:absolute;left:${W + 300}px;top:420px;width:100px;display:-webkit-box;-webkit-box-pack:end;white-space:nowrap;${B}">${R}</div>
      <div id="g8" style="position:absolute;left:300px;top:${H + 400}px;width:40px;height:40px;word-break:break-all;transform:scaleY(-1);transform-origin:0 0;${B}">${R}</div>
      <div id="g9" style="position:absolute;left:500px;top:${H + 400}px;width:100px;white-space:nowrap;offset-path:path('M0 0 L0 -10');offset-rotate:auto;offset-anchor:0 0;${B}">${R}</div>
      <div style="position:absolute;left:-300px;top:-700px;width:100px;transform:rotate(60deg);transform-origin:0 0"><div id="g10" style="width:100px;white-space:nowrap;${B}">${R}</div></div>
      <div style="position:absolute;left:${W + 300}px;top:540px;width:100px;transform:scale3d(-1, 1, 2)"><div id="g11" style="width:100px;white-space:nowrap;${B}">${R}</div></div>
      <div id="g12" style="position:absolute;left:900px;top:${H + 300}px;height:100px;width:40px;writing-mode:sideways-lr;white-space:nowrap;${B}">${R}</div>`);
  });
  await page.waitForTimeout(200);
  const ids = ['g1', 'g2', 'g3', 'g4', 'g5', 'g6', 'g7', 'g8', 'g9', 'g10', 'g11', 'g12'];
  const pts = await visiblePoints(page, ids);
  // 60°の回転は文字の外接矩形の中心が文字に乗らないので、行の上の点を計算する（原点 (-300,-700) から 1000px 進み、行の太さの半分だけ横へ。g3 の帯 x=200〜240 を避ける）
  const a = Math.PI / 3; pts[9] = [Math.round(-300 + 1000 * Math.cos(a) - 20 * Math.sin(a)), Math.round(-700 + 1000 * Math.sin(a) + 20 * Math.cos(a))];
  expect(pts.map((p, i) => p ? 'ok' : ids[i])).toEqual(ids.map(() => 'ok'));   // どの例も画面にかかっている
  const { ref, got } = await sameAsNoCull(page, pts);
  expect(ref.map((c, i) => (c[0] === 255 && c[1] === 255 && c[2] === 255) ? ids[i] : 'drawn')).toEqual(ids.map(() => 'drawn'));
  expect(got).toEqual(ref);
});

for (const who of ['body', 'html']) test(`H-M25 N40: ${who} 自身を反転しても、画面に入るはみ出し文字は間引いても同じに描かれる`, async ({ page }) => {
  await page.evaluate((who) => {
    (who === 'html' ? document.documentElement : document.body).style.transform = 'scaleX(-1)';
    document.body.insertAdjacentHTML('beforeend',
      `<div id="b1" style="position:absolute;left:-900px;top:500px;width:100px;white-space:nowrap;font:40px/40px monospace;color:rgb(0,0,255)">${'█'.repeat(60)}</div>`);
  }, who);
  await page.waitForTimeout(200);
  const pts = await visiblePoints(page, ['b1']);
  expect(pts[0]).not.toBeNull();
  const { ref, got } = await sameAsNoCull(page, pts);
  expect(ref[0]).not.toEqual([255, 255, 255]);
  expect(got).toEqual(ref);
});

test('H-M25 N43: 文字を持つ要素が深く入れ子になった縦書きの2万要素でも、3 秒以内（子孫全体の文字を測ると約 8 秒）', async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 });
  await page.evaluate(() => {
    const nest = (d) => d ? `<div>段${d}${nest(d - 1)}</div>` : '';
    const box = document.createElement('div');
    box.style.writingMode = 'vertical-rl';
    box.innerHTML = Array.from({ length: 100 }, () => nest(200)).join('');
    document.body.append(box);
  });
  await annotate(page, 30, 90, 300, 200, '縦書き');
  const t0 = Date.now();
  await sendAll(page);
  expect(Date.now() - t0).toBeLessThan(3000);
});

test('H-M25b: 2つ目以降の文字ノード（plaintext の改行の後）・3D の回転・3D の上下反転・Range で測った文字の影は、間引いても同じに描かれる（verify-h-m25b N47〜N49）', async ({ page }) => {
  const W = await page.evaluate(() => {
    const W = window.innerWidth, H = window.innerHeight, B = 'font:40px/40px monospace;color:rgb(0,0,255)', R = '█'.repeat(60);
    document.body.insertAdjacentHTML('beforeend', `
      <div id="h1" style="position:absolute;left:${W + 300}px;top:100px;width:100px;white-space:nowrap;unicode-bidi:plaintext;${B}">zz<br>א${R}</div>
      <div style="position:absolute;left:-300px;top:-500px;width:100px;transform:translateZ(1px) rotate(60deg);transform-origin:0 0"><div id="h2" style="width:100px;white-space:nowrap;${B}">${R}</div></div>
      <div id="h3" style="position:absolute;left:1100px;top:${H + 400}px;width:40px;height:40px;word-break:break-all;transform:translateZ(1px) scaleY(-1);transform-origin:0 0;${B}">${R}</div>
      <div id="h4" style="position:absolute;left:${W + 1500}px;top:600px;width:100px;white-space:nowrap;unicode-bidi:plaintext;text-shadow:-500px 0 0 rgb(0,0,255);${B}">א${R}</div>`);
    return W;
  });
  await page.waitForTimeout(200);
  const ids = ['h1', 'h2', 'h3', 'h4'];
  const pts = await visiblePoints(page, ids);
  const a = Math.PI / 3; pts[1] = [Math.round(-300 + 1000 * Math.cos(a) - 20 * Math.sin(a)), Math.round(-500 + 1000 * Math.sin(a) + 20 * Math.cos(a))];
  pts[3] = [W - 50, 620];   // 文字は画面の外（W+400 より右）、影だけが画面に入る
  expect(pts.map((p, i) => p ? 'ok' : ids[i])).toEqual(ids.map(() => 'ok'));
  const { ref, got } = await sameAsNoCull(page, pts);
  expect(ref.map((c, i) => (c[0] === 255 && c[1] === 255 && c[2] === 255) ? ids[i] : 'drawn')).toEqual(ids.map(() => 'drawn'));
  expect(got).toEqual(ref);
});

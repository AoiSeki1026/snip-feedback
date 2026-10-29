// Playwright テスト: 基本操作・状態遷移・受入基準（AC-1〜AC-8、R-14・R-15b・R-16・R-20 など）
import { test, expect } from '@playwright/test';
import { useHost, base, drawBox, sendAll, annotate } from './helpers.mjs';

useHost(test);

test('AC-1/AC-2: 4操作で画像＋テキストが1つの項目でクリップボードに入る', async ({ page }) => {
  // 枠の中心を <p> の中心に合わせる（固定座標だと OS のフォント差で中心が <p> の外に出る）
  const pb = await page.locator('#card-a p').boundingBox();
  const top = Math.round(pb.y + pb.height / 2) - 100;
  await annotate(page, 30, top, 600, top + 200, 'このカードの数字が大きすぎる');
  expect(await page.evaluate(() => window.__feedbackTool.annotations.length)).toBe(1);
  await sendAll(page);
  const p = await page.evaluate(() => { const l = window.__feedbackTool.lastPayload; return { text: l.text, hasPng: !!l.png, cb: l.clipboard, err: l.captureError }; });
  expect(p.err).toBeNull();
  expect(p.hasPng).toBe(true);
  expect(p.cb).toEqual({ image: true, text: true });
  // 送信で注釈は消え、通知が出る (R-05, R-08 r4)
  expect(await page.locator('.fb-box').count()).toBe(0);
  await expect(page.locator('.fb-toast')).toContainText('画像とテキストをコピーしました（1件）');
  await expect(page.locator('button.fb-send')).toHaveText('送信 (0)');
  expect(p.text).toContain('[feedback] ' + base + '/test/host.html');
  expect(p.text).toContain('viewport 1280x800');
  expect(p.text).toContain('1. このカードの数字が大きすぎる');
  expect(p.text).toContain('- 要素: #card-a  data-src=src/CardA.tsx:3');   // 枠の面積の半分以上を覆う最も内側の要素 (R-07 r10)
  expect(p.text).toContain('- 中心の要素: #card-a > p "今月の売上は 1,234,567 円です。"');
  expect(p.text).toContain('- 見出し: "売上"');
  expect(p.text).toContain('- 抜粋: "売上 今月の売上は 1,234,567 円です。');
  expect(p.text).toMatch(/- 枠内の要素: #card-a > h2, #card-a > p, button\[data-testid="refresh"\]/);
  expect(p.text).toContain(`- 枠: x=30 y=${top} w=570 h=200`);
  expect(p.text.trim().endsWith('対象ごとに番号で回答し、迷う点は質問として返してください。')).toBe(true);
  // 1つのクリップボード項目に画像とテキストの両方が入る（Code タブは1回の貼り付けで両方受け取る。H-5）
  const items = await page.evaluate(async () => {
    const items = await navigator.clipboard.read();
    return Promise.all(items.map(async (i) => ({ types: i.types, text: i.types.includes('text/plain') ? await (await i.getType('text/plain')).text() : null })));
  });
  expect(items.length).toBe(1);
  expect(items[0].types).toEqual(expect.arrayContaining(['image/png', 'text/plain']));
  expect(items[0].text).toContain('1. このカードの数字が大きすぎる');
});

test('AC-3: 3件の番号が画像とテキストで一致する', async ({ page }) => {
  await annotate(page, 30, 90, 300, 200, 'いち');
  await annotate(page, 700, 90, 900, 200, 'に');
  await annotate(page, 30, 320, 300, 400, 'さん');
  await expect(page.locator('.fb-label')).toHaveText(['1', '2', '3']);
  await page.locator('button.fb-send').click();
  await page.waitForFunction(() => window.__feedbackTool.lastPayload);
  const { text, w, h } = await page.evaluate(async () => {
    const l = window.__feedbackTool.lastPayload; const bmp = await createImageBitmap(l.png);
    return { text: l.text, w: bmp.width, h: bmp.height };
  });
  expect(text).toContain('1. いち'); expect(text).toContain('2. に'); expect(text).toContain('3. さん');
  expect(w).toBe(1280); expect(h).toBe(800);
  // 赤枠が焼き込まれている: 枠1の上辺中央の画素が赤系
  const px = await page.evaluate(async () => {
    const l = window.__feedbackTool.lastPayload; const bmp = await createImageBitmap(l.png);
    const c = new OffscreenCanvas(bmp.width, bmp.height); const ctx = c.getContext('2d'); ctx.drawImage(bmp, 0, 0);
    return Array.from(ctx.getImageData(165, 90, 1, 1).data);
  });
  expect(px[0]).toBeGreaterThan(180); expect(px[1]).toBeLessThan(90);
  // ボタンでの送信でも注釈は消える
  await page.waitForFunction(() => window.__feedbackTool.state === 'idle');
  expect(await page.locator('.fb-box').count()).toBe(0);
});

test('1段階送信: 送信後は注釈が消え、続けて描いた枠だけが次の送信に入る', async ({ page }) => {
  await annotate(page, 30, 90, 300, 200, 'いち');
  await sendAll(page);
  expect(await page.locator('.fb-box').count()).toBe(0);
  await annotate(page, 700, 90, 900, 200, 'に');
  await sendAll(page);
  const p = await page.evaluate(() => window.__feedbackTool.lastPayload);
  expect(p.count).toBe(1);
  expect(p.text).toContain('1. に');
  expect(p.text).not.toContain('いち');
});

test('AC-6: 8px未満は破棄、Esc は入力中の枠だけ破棄、番号クリックで削除と振り直し', async ({ page }) => {
  await drawBox(page, 100, 100, 105, 104);
  expect(await page.locator('.fb-box').count()).toBe(0);
  expect(await page.locator('textarea.fb-input').count()).toBe(0);

  await annotate(page, 30, 90, 300, 200, '残る');
  await drawBox(page, 700, 90, 900, 200);
  await page.keyboard.type('消える');
  await page.keyboard.press('Escape');
  expect(await page.locator('.fb-box').count()).toBe(1);
  expect(await page.evaluate(() => window.__feedbackTool.annotations.map((a) => a.comment))).toEqual(['残る']);

  await annotate(page, 700, 90, 900, 200, '二番');
  await annotate(page, 30, 320, 300, 400, '三番');
  await page.locator('.fb-label', { hasText: '1' }).click();
  await expect(page.locator('.fb-label')).toHaveText(['1', '2']);
  expect(await page.evaluate(() => window.__feedbackTool.annotations.map((a) => a.comment))).toEqual(['二番', '三番']);
});

test('AC-4: クリップボード画像が拒否されたらフォールバックパネルが出る', async ({ page }) => {
  await page.evaluate(() => { navigator.clipboard.write = () => Promise.reject(new DOMException('denied', 'NotAllowedError')); });
  await annotate(page, 30, 90, 300, 200, '拒否ケース');
  await page.keyboard.press('Control+Enter');
  await expect(page.locator('.fb-panel')).toBeVisible();
  await expect(page.locator('.fb-panel header span')).toContainText('テキストはコピー済み');
  await expect(page.locator('.fb-panel img')).toBeVisible();
  await expect(page.locator('.fb-panel textarea')).toHaveValue(/1\. 拒否ケース/);
  // 閉じると注釈が消える
  await page.locator('.fb-panel button', { hasText: '閉じる' }).click();
  expect(await page.locator('.fb-box').count()).toBe(0);
});

test('AC-5: キャプチャ失敗時はテキストのみ送信して通知', async ({ page }) => {
  await page.evaluate(() => { window.html2canvas = () => Promise.reject(new Error('boom')); });
  await annotate(page, 30, 90, 300, 200, '画像なし');
  await page.keyboard.press('Control+Enter');
  await expect(page.locator('.fb-toast')).toContainText('画像なしで送信しました（1件）');
  const p = await page.evaluate(() => window.__feedbackTool.lastPayload);
  expect(p.png).toBeNull(); expect(p.clipboard.text).toBe(true);
  expect(await page.evaluate(() => navigator.clipboard.readText())).toContain('1. 画像なし');
});

test('AC-8: ホストのキー操作を横取りしない', async ({ page }) => {
  await page.evaluate(() => { window.__hostKeys = []; });
  await page.keyboard.press('Control+Enter'); // 注釈0件 → ホストに届く
  await page.keyboard.press('Control+s');
  await page.keyboard.press('Escape');         // 入力欄なし → ホストに届く
  await page.locator('#memo').click();
  await page.keyboard.type('abc');
  await page.keyboard.press('Enter');
  const keys = await page.evaluate(() => window.__hostKeys);
  expect(keys).toEqual(expect.arrayContaining(['Ctrl+Enter', 'Ctrl+s', 'Escape', 'a', 'b', 'c', 'Enter']));
  expect(await page.locator('#memo').inputValue()).toBe('abc\n');
  // Alt なしのドラッグでは枠が描かれない (R-02)
  await page.mouse.move(30, 90); await page.mouse.down(); await page.mouse.move(300, 200); await page.mouse.up();
  expect(await page.locator('.fb-box').count()).toBe(0);
});

test('枠ボタン: 描画モード中は Alt なしのドラッグで枠が描ける', async ({ page }) => {
  await page.locator('button.fb-draw').click();
  expect(await page.evaluate(() => window.__feedbackTool.drawMode)).toBe(true);
  await page.mouse.move(30, 90); await page.mouse.down(); await page.mouse.move(300, 200, { steps: 3 }); await page.mouse.up();
  await expect(page.locator('textarea.fb-input')).toBeFocused();
  await page.keyboard.type('モードで描いた'); await page.keyboard.press('Enter');
  expect(await page.locator('.fb-box').count()).toBe(1);
  await page.locator('button.fb-draw').click();
  expect(await page.evaluate(() => window.__feedbackTool.drawMode)).toBe(false);
  await page.mouse.move(700, 90); await page.mouse.down(); await page.mouse.move(900, 200); await page.mouse.up();
  expect(await page.locator('.fb-box').count()).toBe(1);
});

// ---------- verify（2026-10-07）の指摘への対応と補強 ----------
test('状態遷移: 描いている途中の Esc で枠を捨て、マウスを離しても入力欄は開かない', async ({ page }) => {
  await page.keyboard.down('Alt');
  await page.mouse.move(30, 90); await page.mouse.down(); await page.mouse.move(300, 200, { steps: 3 });
  await page.keyboard.up('Alt');
  expect(await page.evaluate(() => window.__feedbackTool.state)).toBe('drawing');
  await page.keyboard.press('Escape');
  expect(await page.evaluate(() => window.__feedbackTool.state)).toBe('idle');
  await page.mouse.up();
  expect(await page.locator('.fb-box').count()).toBe(0);
  expect(await page.locator('textarea.fb-input').count()).toBe(0);
});

test('状態遷移: 送信中に番号ラベルを押しても注釈は消えず、送った件数と一致する', async ({ page }) => {
  await page.evaluate(() => { const orig = window.html2canvas; window.html2canvas = (...a) => new Promise((ok) => setTimeout(() => ok(orig(...a)), 800)); });
  await annotate(page, 30, 90, 300, 200, 'いち');
  await annotate(page, 700, 90, 900, 200, 'に');
  await page.keyboard.press('Control+Enter');
  expect(await page.evaluate(() => window.__feedbackTool.state)).toBe('sending');
  await page.locator('.fb-label', { hasText: '1' }).click();
  await page.waitForFunction(() => window.__feedbackTool.lastPayload && window.__feedbackTool.state === 'idle');
  const p = await page.evaluate(() => window.__feedbackTool.lastPayload);
  expect(p.count).toBe(2);
  expect(p.text).toContain('1. いち'); expect(p.text).toContain('2. に');
});

test('R-20 r4: 送信後の Esc はホストに届く（待ち状態は無い）', async ({ page }) => {
  await annotate(page, 30, 90, 300, 200, 'いち');
  await sendAll(page);
  await page.evaluate(() => { window.__hostKeys = []; });
  await page.keyboard.press('Escape');
  expect(await page.evaluate(() => window.__hostKeys)).toContain('Escape');
});

test('R-14 境界: 7px は破棄、8px は入力欄が開く', async ({ page }) => {
  await drawBox(page, 100, 300, 107, 340);
  expect(await page.locator('textarea.fb-input').count()).toBe(0);
  await drawBox(page, 100, 300, 108, 340);
  await expect(page.locator('textarea.fb-input')).toBeFocused();
});

test('AC-3 補強: 3 件の枠の4辺がすべて赤で焼き込まれている', async ({ page }) => {
  const boxes = [[30, 90, 300, 200], [700, 90, 900, 200], [30, 320, 300, 400]];
  for (const [i, r] of boxes.entries()) await annotate(page, ...r, String(i));
  await sendAll(page);
  const bad = await page.evaluate(async (boxes) => {
    const bmp = await createImageBitmap(window.__feedbackTool.lastPayload.png);
    const c = new OffscreenCanvas(bmp.width, bmp.height); const x = c.getContext('2d'); x.drawImage(bmp, 0, 0);
    const red = (px, py) => { const d = x.getImageData(px, py, 1, 1).data; return d[0] > 180 && d[1] < 100 && d[2] < 100; };
    const miss = [];
    for (const [x0, y0, x1, y1] of boxes) {
      for (const t of [0.25, 0.5, 0.75]) {
        const mx = Math.round(x0 + (x1 - x0) * t), my = Math.round(y0 + (y1 - y0) * t);
        if (!red(mx, y0)) miss.push(['top', mx, y0]);
        if (!red(mx, y1)) miss.push(['bottom', mx, y1]);
        if (!red(x0, my)) miss.push(['left', x0, my]);
        if (!red(x1, my)) miss.push(['right', x1, my]);
      }
    }
    return miss;
  }, boxes);
  expect(bad).toEqual([]);
});

test('デシジョンテーブル行4: 画像もテキストも拒否 → 両方手動コピーのパネル', async ({ page }) => {
  await page.evaluate(() => {
    navigator.clipboard.write = () => Promise.reject(new DOMException('denied', 'NotAllowedError'));
    navigator.clipboard.writeText = () => Promise.reject(new DOMException('denied', 'NotAllowedError'));
  });
  await annotate(page, 30, 90, 300, 200, '全部拒否');
  await page.keyboard.press('Control+Enter');
  await expect(page.locator('.fb-panel header span')).toHaveText('手動でコピーしてください');
  await expect(page.locator('.fb-panel img')).toBeVisible();
  await expect(page.locator('.fb-panel textarea')).toHaveValue(/1\. 全部拒否/);
});

test('デシジョンテーブル行6: キャプチャ失敗かつテキスト拒否 → テキストのみのパネル', async ({ page }) => {
  await page.evaluate(() => {
    window.html2canvas = () => Promise.reject(new Error('boom'));
    navigator.clipboard.writeText = () => Promise.reject(new DOMException('denied', 'NotAllowedError'));
  });
  await annotate(page, 30, 90, 300, 200, '画像なし拒否');
  await page.keyboard.press('Control+Enter');
  await expect(page.locator('.fb-panel')).toBeVisible();
  expect(await page.locator('.fb-panel img').count()).toBe(0);
  await expect(page.locator('.fb-panel textarea')).toHaveValue(/1\. 画像なし拒否/);
});

// ---------- verify r4（2026-10-07）の差し戻し対応 ----------
test('状態遷移 r4: 描いている途中の Ctrl+Enter は無視する（drawing × Ctrl+Enter は —）', async ({ page }) => {
  await annotate(page, 30, 90, 300, 200, 'いち');
  await page.keyboard.down('Alt');
  await page.mouse.move(400, 90); await page.mouse.down(); await page.mouse.move(600, 200, { steps: 3 });
  await page.keyboard.up('Alt');
  expect(await page.evaluate(() => window.__feedbackTool.state)).toBe('drawing');
  await page.keyboard.press('Control+Enter');
  await page.waitForTimeout(300);
  expect(await page.evaluate(() => window.__feedbackTool.state)).toBe('drawing');
  expect(await page.evaluate(() => window.__feedbackTool.lastPayload)).toBeNull();
  await page.mouse.up();
  await expect(page.locator('textarea.fb-input')).toBeFocused();
});

// ---------- verify r4c（2026-10-07）への対応・r5 ----------
test('R-16 r5: 一言の入力中に番号ラベルを押しても注釈は消えない（状態遷移表 commenting × 番号クリック = —）', async ({ page }) => {
  await annotate(page, 30, 90, 300, 200, 'いち');
  await drawBox(page, 700, 90, 900, 200);
  await expect(page.locator('textarea.fb-input')).toBeFocused();
  await page.locator('.fb-label', { hasText: '1' }).click();
  expect(await page.evaluate(() => window.__feedbackTool.annotations.map((a) => a.comment))).toEqual(['いち']);
  expect(await page.locator('.fb-label')).toHaveCount(1);
  expect(await page.locator('textarea.fb-input').count()).toBe(1);
  // 入力中のまま続けられる（フォーカスが外れず、Enter で確定できる。R-15/R-04）
  await expect(page.locator('textarea.fb-input')).toBeFocused();
  await page.keyboard.type('に'); await page.keyboard.press('Enter');
  expect(await page.evaluate(() => window.__feedbackTool.annotations.map((a) => a.comment))).toEqual(['いち', 'に']);
});

// ---------- r6（2026-10-07 本人決定） ----------
test('R-15b r6: 入力中にホスト画面をクリックすると、一言を確定扱いで入力欄を閉じ、クリックはホストに届く', async ({ page }) => {
  await page.evaluate(() => { window.__hostClicks = 0; document.addEventListener('mousedown', () => { window.__hostClicks++; }); });
  await drawBox(page, 30, 90, 300, 200);
  await page.keyboard.type('外をクリック');
  await page.mouse.click(700, 600);
  expect(await page.evaluate(() => window.__feedbackTool.state)).toBe('idle');
  expect(await page.locator('textarea.fb-input').count()).toBe(0);
  expect(await page.evaluate(() => window.__feedbackTool.annotations.map((a) => a.comment))).toEqual(['外をクリック']);
  expect(await page.evaluate(() => window.__hostClicks)).toBeGreaterThan(0);
  // 空のまま外をクリックした枠は破棄
  await drawBox(page, 400, 90, 600, 200);
  await page.mouse.click(700, 600);
  expect(await page.locator('textarea.fb-input').count()).toBe(0);
  expect(await page.locator('.fb-box')).toHaveCount(1);
  // その後のキーはホストへ（入力中ではない）
  await page.evaluate(() => { window.__hostKeys = []; });
  await page.keyboard.press('Escape');
  expect(await page.evaluate(() => window.__hostKeys)).toContain('Escape');
});

test('R-15b r6: 入力欄そのもの・ツールのボタンのクリックでは閉じない', async ({ page }) => {
  await drawBox(page, 30, 90, 300, 200);
  await page.keyboard.type('中');
  await page.locator('textarea.fb-input').click();
  expect(await page.locator('textarea.fb-input').count()).toBe(1);
  expect(await page.evaluate(() => window.__feedbackTool.state)).toBe('commenting');
});

// ---------- H-M08（2026-10-07）: verify 持ち越しの補強 ----------
test('H-M08: 送信中に番号ラベルを押しても、画面の注釈は送信が終わるまで減らない（M18）', async ({ page }) => {
  await page.evaluate(() => { const orig = window.html2canvas; window.html2canvas = (...a) => new Promise((ok) => setTimeout(() => ok(orig(...a)), 800)); });
  await annotate(page, 30, 90, 300, 200, 'いち');
  await annotate(page, 700, 90, 900, 200, 'に');
  await page.keyboard.press('Control+Enter');
  await page.locator('.fb-label', { hasText: '1' }).click();
  expect(await page.evaluate(() => window.__feedbackTool.state)).toBe('sending');
  await expect(page.locator('.fb-box')).toHaveCount(2);
  expect(await page.evaluate(() => window.__feedbackTool.annotations.length)).toBe(2);
});

test('H-M08: 入力中にツールの「枠」「送信」以外のボタン（枠）を押しても入力欄は閉じず、フォーカスも残る（M8）', async ({ page }) => {
  await drawBox(page, 30, 90, 300, 200);
  await page.keyboard.type('ボタン');
  await page.locator('button.fb-draw').click();
  expect(await page.locator('textarea.fb-input').count()).toBe(1);
  await expect(page.locator('textarea.fb-input')).toBeFocused();
  await page.keyboard.press('Enter');
  expect(await page.evaluate(() => window.__feedbackTool.annotations.map((a) => a.comment))).toEqual(['ボタン']);
});

test('H-M08: 描画モード中・Alt を押しながらの右クリックでも、入力中なら確定して閉じる（指摘B）', async ({ page }) => {
  await drawBox(page, 30, 90, 300, 200);
  await page.keyboard.type('右');
  await page.keyboard.down('Alt'); await page.mouse.click(700, 600, { button: 'right' }); await page.keyboard.up('Alt');
  expect(await page.locator('textarea.fb-input').count()).toBe(0);
  expect(await page.evaluate(() => window.__feedbackTool.annotations.map((a) => a.comment))).toEqual(['右']);
  await page.locator('button.fb-draw').click();   // 描画モード
  await page.mouse.move(400, 90); await page.mouse.down(); await page.mouse.move(600, 200, { steps: 3 }); await page.mouse.up();
  await page.keyboard.type('中');
  await page.mouse.click(700, 600, { button: 'middle' });
  expect(await page.locator('textarea.fb-input').count()).toBe(0);
  expect(await page.evaluate(() => window.__feedbackTool.annotations.map((a) => a.comment))).toEqual(['右', '中']);
});

test('H-M08: 入力中にホストの iframe の中を押すと、一言を確定して閉じる（指摘A）', async ({ page }) => {
  await page.evaluate((b) => {
    const f = document.createElement('iframe'); f.id = 'clickf'; f.src = b + '/test/plain.html'; f.style.cssText = 'position:absolute;left:700px;top:420px;width:300px;height:150px';
    document.body.append(f);
  }, base);
  await page.waitForFunction(() => document.getElementById('clickf').contentDocument?.readyState === 'complete');
  await drawBox(page, 30, 90, 300, 200);
  await page.keyboard.type('iframe を押す');
  await page.mouse.click(800, 480);
  await expect(page.locator('textarea.fb-input')).toHaveCount(0);
  expect(await page.evaluate(() => window.__feedbackTool.annotations.map((a) => a.comment))).toEqual(['iframe を押す']);
});

// ---------- r8（2026-10-07 本人決定） ----------
test('R-15b r8: スクリプトでフォーカスが iframe へ移っても、一言を確定して閉じる。ツールの「枠」へ Tab で移っても閉じない', async ({ page }) => {
  await page.evaluate((b) => {
    const f = document.createElement('iframe'); f.id = 'ff'; f.src = b + '/test/plain.html'; f.style.cssText = 'position:absolute;left:700px;top:420px;width:300px;height:150px';
    document.body.append(f);
  }, base);
  await page.waitForFunction(() => document.getElementById('ff').contentDocument?.readyState === 'complete');
  await drawBox(page, 30, 90, 300, 200);
  await page.keyboard.type('いち');
  await page.evaluate(() => document.getElementById('ff').focus());
  await expect(page.locator('textarea.fb-input')).toHaveCount(0);
  expect(await page.evaluate(() => window.__feedbackTool.annotations.map((a) => a.comment))).toEqual(['いち']);
  // どんな blur でも確定するわけではない（ツールの部品へ移ったときは残す）
  await drawBox(page, 400, 90, 600, 200);
  await page.keyboard.type('に');
  await page.evaluate(() => document.querySelector('button.fb-draw').focus());
  await page.waitForTimeout(100);
  expect(await page.locator('textarea.fb-input').count()).toBe(1);
});

// ---------- H-M11（2026-10-07） ----------
test('R-15b H-M11: Shadow DOM の中の iframe・ホストの object を押したとき／フォーカスが移ったときも、一言を確定して閉じる', async ({ page }) => {
  await page.evaluate((b) => {
    const h = document.createElement('div'); h.id = 'sh'; h.style.cssText = 'position:absolute;left:700px;top:420px'; document.body.append(h);
    h.attachShadow({ mode: 'open' }).innerHTML = `<iframe id="sf" src="${b}/test/plain.html" style="width:300px;height:120px"></iframe>`;
    const o = document.createElement('object'); o.id = 'ob'; o.type = 'text/html'; o.data = b + '/test/plain.html'; o.style.cssText = 'position:absolute;left:700px;top:600px;width:300px;height:120px';
    document.body.append(o);
  }, base);
  await page.waitForFunction(() => document.getElementById('sh').shadowRoot.getElementById('sf').contentDocument?.readyState === 'complete' && document.getElementById('ob').contentDocument?.readyState === 'complete');
  // Shadow DOM 内の iframe をクリック
  await drawBox(page, 30, 90, 300, 200);
  await page.keyboard.type('影の iframe');
  await page.mouse.click(800, 470);
  await expect(page.locator('textarea.fb-input')).toHaveCount(0);
  // Shadow DOM 内の iframe へスクリプトでフォーカス
  await drawBox(page, 400, 90, 600, 200);
  await page.keyboard.type('影へフォーカス');
  await page.evaluate(() => document.getElementById('sh').shadowRoot.getElementById('sf').focus());
  await expect(page.locator('textarea.fb-input')).toHaveCount(0);
  // object の中をクリック
  await drawBox(page, 30, 300, 300, 380);
  await page.keyboard.type('object');
  await page.mouse.click(800, 650);
  await expect(page.locator('textarea.fb-input')).toHaveCount(0);
  expect(await page.evaluate(() => window.__feedbackTool.annotations.map((a) => a.comment))).toEqual(['影の iframe', '影へフォーカス', 'object']);
});

// ---------- H-M13（2026-10-07） ----------
test('R-15b H-M13: ホストの embed を押しても一言を確定して閉じる', async ({ page }) => {
  await page.evaluate((b) => {
    const e = document.createElement('embed'); e.id = 'em'; e.type = 'text/html'; e.src = b + '/test/plain.html';
    e.style.cssText = 'position:absolute;left:700px;top:420px;width:300px;height:150px'; document.body.append(e);
  }, base);
  await page.waitForTimeout(800);
  await drawBox(page, 30, 90, 300, 200);
  await page.keyboard.type('embed');
  await page.mouse.click(800, 480);
  await expect(page.locator('textarea.fb-input')).toHaveCount(0);
  expect(await page.evaluate(() => window.__feedbackTool.annotations.map((a) => a.comment))).toEqual(['embed']);
});

// ---------- H-M21 c（2026-10-08）: 段階3の計測を楽にする（操作は変えない） ----------
test('H-M21c: 送信の通知に、最初の枠を描き始めてから送信までの所要時間を添える（次の送信では数え直す）', async ({ page }) => {
  await annotate(page, 30, 90, 300, 200, 'いち');
  await page.waitForTimeout(1200);
  await annotate(page, 700, 90, 900, 200, 'に');
  await sendAll(page);
  await expect(page.locator('.fb-toast').last()).toContainText(/画像とテキストをコピーしました（2件）所要 [0-9]+秒/);
  const e1 = await page.evaluate(() => window.__feedbackTool.lastPayload.elapsedMs);
  expect(e1).toBeGreaterThanOrEqual(1200);
  // 次の送信は、新しく描き始めたところから数える
  await annotate(page, 30, 90, 300, 200, 'さん');
  await sendAll(page);
  const e2 = await page.evaluate(() => window.__feedbackTool.lastPayload.elapsedMs);
  expect(e2).toBeLessThan(e1);
  expect(await page.evaluate(() => window.__feedbackTool.lastPayload.count)).toBe(1);
});

test('H-M21c: 所要時間は 1 分以上なら「分」も付ける（表示の形）', async ({ page }) => {
  expect(await page.evaluate(() => [window.__feedbackTool.formatElapsed(5000), window.__feedbackTool.formatElapsed(83000), window.__feedbackTool.formatElapsed(3600000)]))
    .toEqual(['5秒', '1分23秒', '60分0秒']);
});

test('H-M21c F1: 枠をすべて捨ててから描き直すと、所要時間は描き直したところから数える', async ({ page }) => {
  // 入力中に Esc で捨てる
  await drawBox(page, 30, 90, 300, 200);
  await page.keyboard.press('Escape');
  // 8px 未満で捨てる
  await drawBox(page, 400, 90, 404, 94);
  // 番号ラベルで全部消す
  await annotate(page, 700, 90, 900, 200, '消す');
  await page.locator('.fb-label', { hasText: '1' }).click();
  expect(await page.evaluate(() => window.__feedbackTool.annotations.length)).toBe(0);
  await page.waitForTimeout(1500);
  await annotate(page, 30, 90, 300, 200, '描き直し');
  await sendAll(page);
  expect(await page.evaluate(() => window.__feedbackTool.lastPayload.elapsedMs)).toBeLessThan(1500);
});

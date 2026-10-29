// Playwright テスト: 配布サーバ・起動（ブックマークレット、serve.mjs、launch.vbs、版数・dist のバナー）
import { test, expect } from '@playwright/test';
import { createServer } from 'node:http';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { useHost, ROOT, base, P, startServe, waitFor, healthy, runVbs13, runVbsEnv, stopAt } from './helpers.mjs';

useHost(test);

test('ブックマークレット: npm run serve の配信から既存ページへ後付けで読み込める', async ({ browser }) => {
  const { spawn } = await import('node:child_process');
  const { readFileSync } = await import('node:fs');
  const port = (await P(8791));
  const srv = spawn(process.execPath, [join(ROOT, 'serve.mjs')], { env: { ...process.env, FB_PORT: String(port) }, stdio: 'pipe' });
  try {
    await new Promise((ok) => srv.stdout.on('data', (d) => { if (String(d).includes('配信中')) ok(); }));
    const page = await browser.newPage();
    await page.goto(`${base}/test/plain.html`);
    expect(await page.evaluate(() => !!window.__feedbackTool)).toBe(false);
    const bm = readFileSync(join(ROOT, 'docs', 'bookmarklet.txt'), 'utf8').trim().replace('8799', String(port));
    await page.evaluate(decodeURIComponent(bm.replace(/^javascript:/, '')));
    await page.waitForFunction(() => !!window.__feedbackTool);
    await expect(page.locator('button.fb-draw')).toBeVisible();
    await page.close();
  } finally { srv.kill(); }
});

// ---------- r4（2026-10-07 本人決定） ----------
test('版数: __feedbackTool.version と dist のバナーが package.json と一致する', async ({ page }) => {
  const { version } = JSON.parse(readFileSync(join(ROOT, 'package.json'), 'utf8'));
  expect(await page.evaluate(() => window.__feedbackTool.version)).toBe(version);
  expect(readFileSync(join(ROOT, 'dist', 'feedback.js'), 'utf8').startsWith(`/* feedback.js v${version} `)).toBe(true);
});

// ---------- H-M08（2026-10-07）: verify 持ち越しの補強 ----------
test('H-M08: dist のバナーは「v<版数> — 視覚…」（空白あり）', async () => {
  const { version } = JSON.parse(readFileSync(join(ROOT, 'package.json'), 'utf8'));
  expect(readFileSync(join(ROOT, 'dist', 'feedback.js'), 'utf8').startsWith(`/* feedback.js v${version} — 視覚フィードバック入力ツール`)).toBe(true);
});

// ---------- H-M10（2026-10-07）: 簡単に起動できるようにする ----------
test('起動: 案内ページにブックマークレットと試し描きがあり、health と停止が効く', async ({ page }) => {
  const port = (await P(8792));
  const s1 = await startServe(port, ['--launch']);
  try {
    expect(await waitFor(async () => !!(await healthy(port)))).toBe(true);
    const { version } = JSON.parse(readFileSync(join(ROOT, 'package.json'), 'utf8'));
    expect(await healthy(port)).toEqual({ app: 'feedback-system', version });
    expect(s1.out()).toContain(`開く: http://127.0.0.1:${port}/`);   // 開く URL（FB_NO_OPEN ではブラウザを開かず表示だけ）
    await page.goto(`http://127.0.0.1:${port}/`);
    const href = await page.locator('a#bm').getAttribute('href');
    expect(href.startsWith('javascript:')).toBe(true);
    expect(href).toContain(`127.0.0.1:${port}/feedback.js`);
    await page.waitForFunction(() => !!window.__feedbackTool);   // 案内ページでそのまま試せる
    // 停止: 合図のヘッダが無い POST（よそのページからのフォーム送信など）は断る
    expect((await fetch(`http://127.0.0.1:${port}/stop`, { method: 'POST' })).status).toBe(403);
    expect(await healthy(port)).not.toBeNull();
    await page.locator('button#stop').click();
    expect(await Promise.race([s1.exited, new Promise((r) => setTimeout(() => r('timeout'), 5000))])).toBe(0);
  } finally { s1.p.kill(); }
});

test('起動: 2回目の起動は既存のサーバを使ってすぐ終わる。よそのアプリがポートを使っていれば知らせて終わる', async () => {
  const port = (await P(8793));
  const s1 = await startServe(port, ['--launch']);
  try {
    expect(await waitFor(async () => !!(await healthy(port)))).toBe(true);
    const s2 = await startServe(port, ['--launch']);
    expect(await Promise.race([s2.exited, new Promise((r) => setTimeout(() => r('timeout'), 5000))])).toBe(0);
    expect(s2.out()).toContain('起動済み');
    expect(s2.out()).toContain(`開く: http://127.0.0.1:${port}/`);   // 2回目も案内ページを開く
    expect(await healthy(port)).not.toBeNull();   // 1つ目は動いたまま
  } finally { s1.p.kill(); }
  // よそのアプリ（health に答えない）がポートを使っている
  const other = createServer((req, res) => { res.writeHead(404); res.end(); });
  { const lp = await P(8794); await new Promise((r) => other.listen(lp, '127.0.0.1', r)); }
  try {
    const s3 = await startServe((await P(8794)), ['--launch']);
    expect(await Promise.race([s3.exited, new Promise((r) => setTimeout(() => r('timeout'), 5000))])).toBe(1);
    expect(s3.out()).toContain(String(await P(8794)));
  } finally { other.close(); }
});

test('起動 r7b: Host が 127.0.0.1／localhost 以外のリクエストは断る（DNS リバインディング対策）', async () => {
  const port = (await P(8795));
  const s1 = await startServe(port, ['--launch']);
  const raw = (path, method, headers) => new Promise((ok, ng) => {
    import('node:http').then(({ request }) => {
      const r = request({ host: '127.0.0.1', port, path, method, headers }, (res) => { res.resume(); ok(res.statusCode); });
      r.on('error', ng); r.end();
    });
  });
  try {
    expect(await waitFor(async () => !!(await healthy(port)))).toBe(true);
    expect(await raw('/stop', 'POST', { Host: `attacker.example:${port}`, 'X-FB-Stop': '1' })).toBe(403);
    expect(await raw('/', 'GET', { Host: 'attacker.example' })).toBe(403);
    expect(await raw('/health', 'GET', { Host: `localhost:${port}` })).toBe(200);
    expect(await healthy(port)).not.toBeNull();   // 止まっていない
  } finally { s1.p.kill(); }
});

test('起動 r7b: bookmarklet.txt が読めなくても案内ページは落ちない', async () => {
  const { mkdtempSync, cpSync, rmSync } = await import('node:fs');
  const { tmpdir } = await import('node:os');
  const dir = mkdtempSync(join(tmpdir(), 'fb-srv-'));
  for (const f of ['serve.mjs', 'package.json']) cpSync(join(ROOT, f), join(dir, f));
  cpSync(join(ROOT, 'dist'), join(dir, 'dist'), { recursive: true });
  const { spawn } = await import('node:child_process');
  const port = (await P(8796));
  const p = spawn(process.execPath, [join(dir, 'serve.mjs')], { env: { ...process.env, FB_PORT: String(port), FB_NO_OPEN: '1' }, stdio: 'pipe' });
  try {
    expect(await waitFor(async () => !!(await healthy(port)))).toBe(true);
    const r = await fetch(`http://127.0.0.1:${port}/`);
    expect(r.status).toBe(200);
    expect(await r.text()).toContain('bookmarklet.txt');   // 読めない旨を案内に出す
    expect(await healthy(port)).not.toBeNull();
  } finally { p.kill(); rmSync(dir, { recursive: true, force: true }); }
});

test('起動 r7b: ブックマークレットの読み込み失敗時の案内はショートカットを指す', async () => {
  const bm = readFileSync(join(ROOT, 'docs', 'bookmarklet.txt'), 'utf8');
  expect(bm).toContain('画面赤枠フィードバック');
  expect(bm).not.toContain('npm run serve');
});

// ---------- H-M11（2026-10-07） ----------
test('起動 H-M11: Host の大文字・小文字は区別しない（LOCALHOST でも通す）', async () => {
  const port = (await P(8797));
  const s1 = await startServe(port, ['--launch']);
  const raw = (headers) => new Promise((ok, ng) => {
    import('node:http').then(({ request }) => {
      const r = request({ host: '127.0.0.1', port, path: '/health', method: 'GET', headers }, (res) => { res.resume(); ok(res.statusCode); });
      r.on('error', ng); r.end();
    });
  });
  try {
    expect(await waitFor(async () => !!(await healthy(port)))).toBe(true);
    expect(await raw({ Host: `LOCALHOST:${port}` })).toBe(200);
    expect(await raw({ Host: `Example.COM:${port}` })).toBe(403);
  } finally { s1.p.kill(); }
});

test('起動 H-M11: ショートカット用の launch.vbs は、起動できなければ知らせ、起動できれば知らせない', async () => {
  const { spawn } = await import('node:child_process');
  const runVbs = (port) => new Promise((ok) => {
    const p = spawn('cscript', ['//nologo', join(ROOT, 'tools', 'launch.vbs'), process.execPath],
      { env: { ...process.env, FB_PORT: String(port), FB_NO_OPEN: '1', FB_VBS_TEST: '1' }, stdio: 'pipe' });
    let out = ''; p.stdout.on('data', (d) => { out += d; });
    p.on('exit', (code) => ok({ code, out }));
  });
  // よそのアプリがポートを使っている → 知らせる
  const other = createServer((req, res) => { res.writeHead(404); res.end(); });
  { const lp = await P(8798); await new Promise((r) => other.listen(lp, '127.0.0.1', r)); }
  try {
    const r1 = await runVbs((await P(8798)));
    expect(r1.out).toContain('FB_LAUNCH_FAILED');
  } finally { other.close(); }
  // 空いている → 起動して知らせない
  const port = (await P(8789));
  const r2 = await runVbs(port);
  try {
    expect(r2.out).toContain('FB_LAUNCH_OK');
    expect(await healthy(port)).not.toBeNull();
  } finally {
    await fetch(`http://127.0.0.1:${port}/stop`, { method: 'POST', headers: { 'X-FB-Stop': '1' } }).catch(() => {});
  }
});

// ---------- H-M13（2026-10-07） ----------
test('起動 H-M13: launch.vbs は失敗の原因を速く正しく知らせる（ポート使用中・起動エラー・node なし）', async () => {
  // よそのアプリがポートを使っている → 原因はポート使用中、5 秒以内
  const other = createServer((req, res) => { res.writeHead(404); res.end(); });
  { const lp = await P(8788); await new Promise((r) => other.listen(lp, '127.0.0.1', r)); }
  try {
    const r1 = await runVbs13((await P(8788)));
    expect(r1.out).toContain('FB_LAUNCH_FAILED:PORT_IN_USE');
    expect(r1.ms).toBeLessThan(5000);
  } finally { other.close(); }
  // 配布サーバ自体が起動に失敗（範囲外のポート）→ 原因はエラー、5 秒以内（ポート使用中とは言わない）
  const r2 = await runVbs13(99999);
  expect(r2.out).toContain('FB_LAUNCH_FAILED:ERROR');
  expect(r2.ms).toBeLessThan(5000);
  // node が見つからない → 原因は node なし、すぐ
  const r3 = await runVbs13((await P(8787)), 'C:\\nowhere\\node.exe');
  expect(r3.out).toContain('FB_LAUNCH_FAILED:NO_NODE');
  expect(r3.ms).toBeLessThan(3000);
});

test('起動 H-M13: launch.vbs は2回目（起動済み）も成功として扱う', async () => {
  const port = (await P(8786));
  const r1 = await runVbs13(port);
  try {
    expect(r1.out).toContain('FB_LAUNCH_OK');
    const r2 = await runVbs13(port);
    expect(r2.out).toContain('FB_LAUNCH_OK');
    expect(r2.ms).toBeLessThan(5000);
  } finally {
    await fetch(`http://127.0.0.1:${port}/stop`, { method: 'POST', headers: { 'X-FB-Stop': '1' } }).catch(() => {});
  }
});

test('起動 H-M13: health に答えても本ツールでないアプリなら「起動済み」とみなさない', async () => {
  const other = createServer((req, res) => { res.writeHead(200, { 'content-type': 'application/json' }); res.end(JSON.stringify({ app: 'other-app' })); });
  { const lp = await P(8785); await new Promise((r) => other.listen(lp, '127.0.0.1', r)); }
  try {
    const s3 = await startServe((await P(8785)), ['--launch']);
    expect(await Promise.race([s3.exited, new Promise((r) => setTimeout(() => r('timeout'), 5000))])).toBe(1);
  } finally { other.close(); }
});

// ---------- H-M14（2026-10-07） ----------
test('起動 H-M14 N14: ショートカットをほぼ同時に2回押しても、スクリプトエラーにならず両方成功として終わる', async () => {
  const port = (await P(8784));
  test.setTimeout(90000);
  try {
    for (let i = 0; i < 6; i++) {   // タイミング次第で起きるので何度か試す
      await stopAt(port);
      await waitFor(async () => !(await healthy(port)), 3000);
      const [a, b] = await Promise.all([runVbsEnv(port), runVbsEnv(port)]);
      for (const r of [a, b]) {
        expect(r.out).not.toMatch(/Microsoft VBScript|実行時エラー|runtime error/i);
        expect(r.out).toContain('FB_LAUNCH_OK');
      }
    }
  } finally { await stopAt(port); }
});

test('起動 H-M14 N15: 一時フォルダに書けなくても、配布サーバが応答すれば成功として扱う', async () => {
  const port = (await P(8783));
  try {
    const r = await runVbsEnv(port, { TEMP: 'Z:\\no\\such\\dir', TMP: 'Z:\\no\\such\\dir' });
    expect(r.out).toContain('FB_LAUNCH_OK');
    expect(r.ms).toBeLessThan(5000);
  } finally { await stopAt(port); }
});

test('起動 H-M14 N16: 前回の結果ファイル（OK）が残っていても、今回の結果（ポート使用中）で知らせる', async () => {
  const { writeFileSync } = await import('node:fs');
  const { tmpdir } = await import('node:os');
  const port = (await P(8782));
  writeFileSync(join(tmpdir(), `feedback-system-launch-${port}.txt`), 'OK', 'utf8');   // 前回の残り
  const other = createServer((req, res) => { res.writeHead(404); res.end(); });
  await new Promise((r) => other.listen(port, '127.0.0.1', r));
  try {
    const r = await runVbsEnv(port);
    expect(r.out).toContain('FB_LAUNCH_FAILED:PORT_IN_USE');
  } finally { other.close(); await stopAt(port); }
});

// ---------- verify-h-m07 の差し戻し対応 ----------
test('起動 N17: 別アプリがポートを使っている状態で2回同時に押しても、両方「ポート使用中」と知らせる', async () => {
  test.setTimeout(120000);
  const port = await P(9701);
  const other = createServer((req, res) => { res.writeHead(404); res.end(); });
  await new Promise((r) => other.listen(port, '127.0.0.1', r));
  try {
    for (let i = 0; i < 6; i++) {
      const [a, b] = await Promise.all([runVbsEnv(port), runVbsEnv(port)]);
      expect(a.out).toContain('FB_LAUNCH_FAILED:PORT_IN_USE');
      expect(b.out).toContain('FB_LAUNCH_FAILED:PORT_IN_USE');
    }
  } finally { other.close(); }
});

// ---------- H-M18（2026-10-08） ----------
test('起動 H-M18 D・E: 一時フォルダに書けず、別アプリ（名前が似ていても）がポートを使っているときは「ポート使用中」と知らせる', async () => {
  const port = await P(9702);
  const other = createServer((req, res) => { res.writeHead(200, { 'content-type': 'application/json' }); res.end(JSON.stringify({ app: 'other-app', tool: 'feedback-system' })); });   // 旧の判定（"feedback-system" を含むか）では誤って成功になる形（verify-h-m18 N22）
  await new Promise((r) => other.listen(port, '127.0.0.1', r));
  try {
    const r = await runVbsEnv(port, { TEMP: 'Z:\\no\\such\\dir', TMP: 'Z:\\no\\such\\dir' });
    expect(r.out).toContain('FB_LAUNCH_FAILED:PORT_IN_USE');
    expect(r.ms).toBeLessThan(5000);
  } finally { other.close(); }
});

// ---------- H-M22（2026-10-08）: 独立したウィンドウ、閉じたら配布サーバも止まる ----------
const openEvents = async (port) => {
  const ac = new AbortController();
  const r = await fetch(`http://127.0.0.1:${port}/events`, { signal: ac.signal });
  expect(r.headers.get('content-type')).toContain('text/event-stream');
  const reader = r.body.getReader();
  reader.read().catch(() => {});
  return () => { ac.abort(); };
};
const exitedWithin = (s, ms) => Promise.race([s.exited, new Promise((r) => setTimeout(() => r('timeout'), ms))]);

test('起動 H-M22: --launch では、案内ページ（/events の接続）がすべて閉じたら配布サーバが止まる。開く前は止まらない', async () => {
  const port = await P(9801);
  const s1 = await startServe(port, ['--launch']);
  try {
    expect(await waitFor(async () => !!(await healthy(port)))).toBe(true);
    expect(await exitedWithin(s1, 4000)).toBe('timeout');          // まだ誰も開いていないので止まらない
    const close1 = await openEvents(port);
    const close2 = await openEvents(port);                          // 2つ開いている
    close1();
    expect(await exitedWithin(s1, 4000)).toBe('timeout');          // 1つ残っているので止まらない
    close2();
    expect(await exitedWithin(s1, 8000)).toBe(0);                  // すべて閉じたら止まる
  } finally { s1.p.kill(); }
});

test('起動 H-M22: 閉じてもすぐ開き直せば（再読み込み）止まらない。npm run serve（--launch なし）では止まらない', async () => {
  const port = await P(9802);
  const s1 = await startServe(port, ['--launch']);
  try {
    expect(await waitFor(async () => !!(await healthy(port)))).toBe(true);
    const close1 = await openEvents(port);
    close1();
    const close2 = await openEvents(port);                          // すぐ開き直す
    expect(await exitedWithin(s1, 5000)).toBe('timeout');
    close2();
    expect(await exitedWithin(s1, 8000)).toBe(0);
  } finally { s1.p.kill(); }
  const port2 = await P(9803);
  const s2 = await startServe(port2, []);
  try {
    expect(await waitFor(async () => !!(await healthy(port2)))).toBe(true);
    const c = await openEvents(port2); c();
    expect(await exitedWithin(s2, 6000)).toBe('timeout');
  } finally { s2.p.kill(); }
});

test('起動 H-M22: 案内ページはウィンドウを閉じると止まることを示し、ブックマークレットのコピーができる。ページを閉じると止まる', async ({ browser }) => {
  const port = await P(9804);
  const s1 = await startServe(port, ['--launch']);
  try {
    expect(await waitFor(async () => !!(await healthy(port)))).toBe(true);
    const ctx = await browser.newContext();
    await ctx.grantPermissions(['clipboard-read', 'clipboard-write'], { origin: `http://127.0.0.1:${port}` });
    const page = await ctx.newPage();
    await page.goto(`http://127.0.0.1:${port}/`);
    await expect(page.locator('body')).toContainText('このウィンドウを閉じると配信を止めます');
    await page.locator('details#register > summary').click();
    await page.locator('button#copy-bm').click();
    const bm = readFileSync(join(ROOT, 'docs', 'bookmarklet.txt'), 'utf8').trim().replace('127.0.0.1:8799', `127.0.0.1:${port}`);
    expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(bm);
    await ctx.close();                                               // ウィンドウを閉じる
    expect(await exitedWithin(s1, 8000)).toBe(0);
  } finally { s1.p.kill(); }
});

test('起動 H-M22b F2: よそのページ（別オリジン）からの /events は数えず、閉じても配信は止まらない', async ({ page }) => {
  const port = await P(9805);
  const s1 = await startServe(port, ['--launch']);
  try {
    expect(await waitFor(async () => !!(await healthy(port)))).toBe(true);
    // ブラウザが付ける Sec-Fetch-Site がよそ（cross-site／same-site）なら断る
    const r = await fetch(`http://127.0.0.1:${port}/events`, { headers: { 'Sec-Fetch-Site': 'cross-site' } });
    expect(r.status).toBe(403);
    // 実際のよそのページ（テスト用ホスト＝別ポート）から EventSource を開いて閉じる
    await page.evaluate((u) => { window.__es = new EventSource(u); }, `http://127.0.0.1:${port}/events`);
    await page.waitForTimeout(500);
    await page.evaluate(() => window.__es.close());
    await page.goto('about:blank');
    expect(await Promise.race([s1.exited, new Promise((res) => setTimeout(() => res('timeout'), 6000))])).toBe('timeout');
  } finally { s1.p.kill(); }
});

test("起動 H-M22c F2': よそのページが案内ページを iframe や window.open で開いて閉じても、配信は止まらない", async ({ page }) => {
  const port = await P(9806);
  const s1 = await startServe(port, ['--launch']);
  try {
    expect(await waitFor(async () => !!(await healthy(port)))).toBe(true);
    const url = `http://127.0.0.1:${port}/`;
    // iframe で開いて消す
    await page.evaluate((u) => { const f = document.createElement('iframe'); f.id = 'gf'; f.src = u; document.body.append(f); }, url);
    await page.waitForTimeout(1500);
    await page.evaluate(() => document.getElementById('gf').remove());
    // window.open で開いて閉じる
    const [popup] = await Promise.all([page.waitForEvent('popup'), page.evaluate((u) => { window.__w = window.open(u); }, url)]);
    await popup.waitForLoadState();
    await page.waitForTimeout(800);
    await popup.close();
    expect(await Promise.race([s1.exited, new Promise((res) => setTimeout(() => res('timeout'), 6000))])).toBe('timeout');
  } finally { s1.p.kill(); }
});

// ---------- H-M23（2026-10-08）: ブックマークレットは固定（登録は最初の1回だけ） ----------
test('H-M23: ブックマークレット（docs/bookmarklet.txt）の中身は変えない。変えると登録し直しが要るため、このテストで止める', async () => {
  const { createHash } = await import('node:crypto');
  const sha = createHash('sha256').update(readFileSync(join(ROOT, 'docs', 'bookmarklet.txt'), 'utf8').trim()).digest('hex');   // 改行コードの違い（CRLF/LF）は無視
  // ツール本体の更新は配布サーバ（/feedback.js）から毎回届くので、ブックマークレット自体を変える必要は無い。
  // どうしても変えるときは、本人に「登録し直し」が要ることを知らせてから、この値を更新する
  expect(sha).toBe('6977bb437c8f9d6b2a92e1c5f8b5a98d81947107cfa73f42a4d988aad4a7c5d8');
});

// ---------- H-M26（2026-10-09）: 案内ページを今の運用に合わせる ----------
test('起動 H-M26: 使い方の最初は「Chrome のブックマークバーの赤枠を押す」、開いておく案内があり、登録は最初の1回だけの畳んだ欄にある', async ({ page }) => {
  const port = await P(9811);
  const s1 = await startServe(port, ['--launch']);
  try {
    expect(await waitFor(async () => !!(await healthy(port)))).toBe(true);
    await page.goto(`http://127.0.0.1:${port}/`);
    const h2 = await page.locator('h2').allTextContents();
    expect(h2[0]).toBe('1. 使い方');
    expect(h2[h2.length - 1]).toContain('最初の1回だけ');
    await expect(page.locator('ol > li').first()).toContainText('ブックマークバーの「赤枠」を押す');
    await expect(page.locator('#keep-open')).toBeVisible();
    await expect(page.locator('a#bm')).toBeHidden();                       // 畳んである
    await page.locator('details#register > summary').click();
    await expect(page.locator('a#bm')).toBeVisible();                      // 開けばドラッグできる
    const bm = readFileSync(join(ROOT, 'docs', 'bookmarklet.txt'), 'utf8').trim().replace('127.0.0.1:8799', `127.0.0.1:${port}`);
    expect(await page.locator('a#bm').getAttribute('href')).toBe(bm);      // 中身は変えない
    // よそのページから開かれた案内ページは数えないので、閉じると止まる旨を出さない（N52）
    const html = await (await fetch(`http://127.0.0.1:${port}/`, { headers: { 'Sec-Fetch-Site': 'cross-site' } })).text();
    expect(html).not.toContain('keep-open');
    expect(html).not.toContain('このウィンドウを閉じると配信を止めます');
  } finally { s1.p.kill(); }
  const port2 = await P(9812);
  const s2 = await startServe(port2, []);
  try {
    expect(await waitFor(async () => !!(await healthy(port2)))).toBe(true);
    await page.goto(`http://127.0.0.1:${port2}/`);
    await expect(page.locator('#keep-open')).toHaveCount(0);               // npm run serve では閉じても止まらないので出さない
    await expect(page.locator('ol > li').first()).not.toContainText('ブックマークバーが無い');   // 普段のブラウザのタブで開くのでバーがある（N51）
  } finally { s2.p.kill(); }
});

// H-M38（r16 R-23・AC-12）: 案内ページのアイコンは v2 と同じ app.ico（タブ・タスクバー）
test('H-M38: 案内ページは v2 と同じアイコンを favicon として出す', async ({ page }) => {
  const port = (await P(8793));
  const s1 = await startServe(port, ['--launch']);
  try {
    expect(await waitFor(async () => !!(await healthy(port)))).toBe(true);
    const r = await fetch(`http://127.0.0.1:${port}/favicon.ico`);
    expect(r.status).toBe(200);
    expect(r.headers.get('content-type')).toBe('image/x-icon');
    expect(Buffer.from(await r.arrayBuffer()).equals(readFileSync(join(ROOT, 'win', 'src', 'SnipFeedback', 'app.ico')))).toBe(true);
    await page.goto(`http://127.0.0.1:${port}/`);
    expect(await page.locator('link[rel="icon"]').getAttribute('href')).toBe('/favicon.ico');
  } finally { s1.p.kill(); }
});

// verify-h-m38 N6: よその Host の /favicon.ico は断る（DNS リバインディング対策が先に効く）
test('H-M38 N6: よその Host の /favicon.ico は 403', async () => {
  const port = (await P(8794));
  const s1 = await startServe(port, ['--launch']);
  const raw = (path, headers) => new Promise((ok, ng) => {
    import('node:http').then(({ request }) => {
      const r = request({ host: '127.0.0.1', port, path, method: 'GET', headers }, (res) => { res.resume(); ok(res.statusCode); });
      r.on('error', ng); r.end();
    });
  });
  try {
    expect(await waitFor(async () => !!(await healthy(port)))).toBe(true);
    expect(await raw('/favicon.ico', { Host: `attacker.example:${port}` })).toBe(403);
    expect(await raw('/favicon.ico', { Host: `localhost:${port}` })).toBe(200);
  } finally { s1.p.kill(); }
});

// verify-h-m38 N6: ショートカットのアイコンは v2 の app.ico（R-23）
test('H-M38 N6: install-shortcuts.ps1 は v2 の app.ico を指し、そのファイルがある', async () => {
  const src = readFileSync(join(ROOT, 'tools', 'install-shortcuts.ps1'), 'utf8');
  expect(src).toContain(String.raw`$icon = Join-Path $root 'win\src\SnipFeedback\app.ico'`);
  expect(src).toContain('$s.IconLocation = "$icon,0"');
  expect(readFileSync(join(ROOT, 'win', 'src', 'SnipFeedback', 'app.ico')).length).toBeGreaterThan(0);
});

// verify-h-m38 N6・N4: 案内の窓の選び方。通常のタブ（Edge・Beta・Dev・Canary、ゼロ幅の空白入り）とよそのツールの窓は選ばない
test('H-M38 N6: set-window-id.ps1 は案内の窓だけを選ぶ', async () => {
  test.skip(process.platform !== 'win32', 'Windows だけ');
  const { spawnSync } = await import('node:child_process');
  const titles = [
    '画面赤枠フィードバック',                                        // 案内の窓（--app）
    '画面赤枠フィードバック - 個人 - Microsoft\u200b Edge',            // 通常のタブ（ゼロ幅の空白入り）
    '画面赤枠フィードバック - Microsoft Edge',
    '画面赤枠フィードバック - 個人 - Microsoft\u200b Edge Beta',
    '画面赤枠フィードバック - Microsoft Edge Dev',
    '画面赤枠フィードバック - Microsoft Edge Canary',
    'スライド赤枠レビュー',                                          // よそのツール
    'メモ 画面赤枠フィードバック',
  ];
  const q = (t) => `'${t.replace(/'/g, "''")}'`;
  const cmd = `& ${q(join(ROOT, 'tools', 'set-window-id.ps1'))} -Title ${q('画面赤枠フィードバック')} -CheckTitle @(${titles.map(q).join(',')})`;
  const r = spawnSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', Buffer.from(cmd, 'utf16le').toString('base64')], { encoding: 'utf8' });
  expect(r.status).toBe(0);
  expect(r.stdout.trim().split(/\r?\n/)).toEqual(['1', '0', '0', '0', '0', '0', '0', '0']);
});

// r17（H-M43、L5）: /feedback.js に CORS の許可を付けない（script タグでの読み込みには要らない。よそのサイトから版と中身を読ませない）
test('R-21 r17: /feedback.js の応答に Access-Control-Allow-Origin が無い', async () => {
  const port = (await P(8780));
  const s1 = await startServe(port, ['--launch']);
  try {
    expect(await waitFor(async () => !!(await healthy(port)))).toBe(true);
    const r = await fetch(`http://127.0.0.1:${port}/feedback.js`);
    expect(r.status).toBe(200);
    expect(r.headers.get('access-control-allow-origin')).toBeNull();
  } finally { s1.p.kill(); }
});

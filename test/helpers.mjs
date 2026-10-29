// テストの共通部分: テスト用 HTTP サーバとフック（useHost）、操作・画素・起動まわりの関数
import { expect } from '@playwright/test';
import { createServer } from 'node:http';
import { readFileSync, existsSync } from 'node:fs';
import { join, extname } from 'node:path';
import { fileURLToPath } from 'node:url';

export const ROOT = fileURLToPath(new URL('..', import.meta.url)); // Windows: .pathname は /C:/… と %エンコードのままになる
let server;
export let base;   // テスト用サーバの URL。ESM のライブバインディングなので、import した側からも beforeAll 後の値が見える

// 各 spec ファイルの先頭で useHost(test) を呼ぶ: テスト用サーバの起動・停止、%TEMP% の片付け、毎テストの host.html 読み込み
export function useHost(test) {
  test.beforeAll(async () => {
    server = createServer((req, res) => {
      const p = join(ROOT, decodeURIComponent(req.url.split('?')[0]));
      if (!existsSync(p)) { res.writeHead(404); return res.end(); }
      const type = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript' }[extname(p)] || 'application/octet-stream';
      res.writeHead(200, { 'content-type': type }); res.end(readFileSync(p));
    });
    await new Promise((r) => server.listen(0, '127.0.0.1', r));
    base = `http://127.0.0.1:${server.address().port}`;
  });
  test.afterAll(async () => {
    server.close();
    // 起動テストが %TEMP% に書いた結果ファイルを片付ける（ポートを毎回変えるので残すと増え続ける。verify-h-m07 N7）
    const { rmSync } = await import('node:fs'); const { tmpdir } = await import('node:os');
    for (const port of [...Object.values(PORTS), 99999]) { try { rmSync(join(tmpdir(), `feedback-system-launch-${port}.txt`), { force: true }); } catch {} }   // 99999 は範囲外ポートの起動エラーのテスト（verify-h-m07d N18）
  });
  test.beforeEach(async ({ context, page }) => {
    await context.grantPermissions(['clipboard-read', 'clipboard-write']);
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto(`${base}/test/host.html`);
    await page.waitForFunction(() => !!window.__feedbackTool);
  });
}

// 起動まわりのテストのポートは毎回空いているものを選ぶ（固定番号だと別のアプリ・別のセッションとぶつかる）
export const PORTS = {};
export async function freePort() {
  const srv = createServer(); await new Promise((r) => srv.listen(0, '127.0.0.1', r));
  const port = srv.address().port; await new Promise((r) => srv.close(r)); return port;
}
export async function P(n) { if (!PORTS[n]) PORTS[n] = await freePort(); return PORTS[n]; }

export async function drawBox(page, x0, y0, x1, y1) {
  await page.keyboard.down('Alt');
  await page.mouse.move(x0, y0);
  await page.mouse.down();
  await page.mouse.move(x1, y1, { steps: 4 });
  await page.mouse.up();
  await page.keyboard.up('Alt');
}
export async function sendAll(page) {
  await page.keyboard.press('Control+Enter');
  await page.waitForFunction(() => window.__feedbackTool.lastPayload && window.__feedbackTool.state === 'idle');
}
export async function annotate(page, x0, y0, x1, y1, comment) {
  await drawBox(page, x0, y0, x1, y1);
  await expect(page.locator('textarea.fb-input')).toBeFocused();
  await page.keyboard.type(comment);
  await page.keyboard.press('Enter');
}

// （verify（2026-10-07）の指摘への対応と補強）
export const rectOf = (page, sel) => page.locator(sel).boundingBox();

// （verify r5（2026-10-07）の差し戻し対応）
export const flexCase = (css, html) => async (page) => {
  await page.evaluate(([c, h]) => {
    const st = document.createElement('style'); st.textContent = c; document.head.append(st);
    const r = document.createElement('div'); r.id = 'flexrow2'; r.innerHTML = h; document.body.append(r);
  }, [css, html]);
  await page.waitForTimeout(300);
  const m = await page.locator('#mkG').boundingBox();
  await annotate(page, 30, 90, 300, 200, 'flex2');
  await sendAll(page);
  return page.evaluate(async (pts) => {
    const bmp = await createImageBitmap(window.__feedbackTool.lastPayload.png);
    const c = new OffscreenCanvas(bmp.width, bmp.height); const x = c.getContext('2d'); x.drawImage(bmp, 0, 0);
    return pts.map(([px, py]) => Array.from(x.getImageData(px, py, 1, 1).data).slice(0, 3));
  }, [[m.x + 1, m.y + 20], [m.x + m.width - 2, m.y + 20], [m.x - 3, m.y + 20]].map(([a, b]) => [Math.round(a), Math.round(b)]));
};

// （H-M08（2026-10-07）: verify 持ち越しの補強）
export const greenUrl = (page) => page.evaluate(() => { const c = document.createElement('canvas'); c.width = c.height = 4; const x = c.getContext('2d'); x.fillStyle = '#00ff00'; x.fillRect(0, 0, 4, 4); return c.toDataURL(); });

// （H-M08（2026-10-07）: verify 持ち越しの補強）
export const pixelsOf = (page, pts) => page.evaluate(async (pts) => {
  const bmp = await createImageBitmap(window.__feedbackTool.lastPayload.png);
  const c = new OffscreenCanvas(bmp.width, bmp.height); const x = c.getContext('2d'); x.drawImage(bmp, 0, 0);
  return pts.map(([px, py]) => Array.from(x.getImageData(Math.round(px), Math.round(py), 1, 1).data).slice(0, 3));
}, pts);

// （H-M08（2026-10-07）: verify 持ち越しの補強）
export const pngServer = async () => {
  const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==', 'base64');
  const srv = createServer((req, res) => {
    if (req.url.startsWith('/404')) { res.writeHead(404); return res.end(); }
    if (req.url.endsWith('.html')) { res.writeHead(200, { 'content-type': 'text/html' }); return res.end('<p>other frame</p>'); }
    res.writeHead(200, { 'content-type': 'image/png', 'access-control-allow-origin': '*' }); res.end(png);
  });
  await new Promise((r) => srv.listen(0, '127.0.0.1', r));
  return { srv, other: `http://localhost:${srv.address().port}` };
};

// （H-M08（2026-10-07）: verify 持ち越しの補強）
export const columnCase = (extra) => async (page) => {
  await page.evaluate((x) => {
    const st = document.createElement('style');
    st.textContent = '#colrow{position:absolute;left:700px;top:300px;width:120px;height:240px;display:flex;flex-direction:column;align-items:flex-start}'
      + ` #colrow iframe{flex:1 1 auto ${x};width:100px;height:50px ${x};border:0} #colrow .mk{flex:1 1 auto;width:40px;height:40px;background:#0000ff}`;
    document.head.append(st);
    const r = document.createElement('div'); r.id = 'colrow'; r.innerHTML = '<iframe srcdoc="x"></iframe><div class="mk" id="mkC"></div>'; document.body.append(r);
  }, extra);
  await page.waitForTimeout(300);
  const m = await page.locator('#mkC').boundingBox();
  await annotate(page, 30, 90, 300, 200, 'column');
  await sendAll(page);
  return pixelsOf(page, [[m.x + 20, m.y + 1], [m.x + 20, m.y + m.height - 2], [m.x + 20, m.y - 3]]);
};

// （H-M10（2026-10-07）: 簡単に起動できるようにする）
export const startServe = async (port, args = []) => {
  const { spawn } = await import('node:child_process');
  const p = spawn(process.execPath, [join(ROOT, 'serve.mjs'), ...args], { env: { ...process.env, FB_PORT: String(port), FB_NO_OPEN: '1' }, stdio: 'pipe' });
  let out = '';
  p.stdout.on('data', (d) => { out += d; }); p.stderr.on('data', (d) => { out += d; });
  const exited = new Promise((ok) => p.on('exit', (code) => ok(code)));
  return { p, exited, out: () => out };
};

// （H-M10（2026-10-07）: 簡単に起動できるようにする）
export const waitFor = async (fn, ms = 5000) => { const end = Date.now() + ms; while (Date.now() < end) { if (await fn()) return true; await new Promise((r) => setTimeout(r, 100)); } return false; };

// （H-M10（2026-10-07）: 簡単に起動できるようにする）
export const healthy = (port) => fetch(`http://127.0.0.1:${port}/health`).then((r) => r.ok ? r.json() : null).catch(() => null);

// （H-M13（2026-10-07））
export const runVbs13 = async (port, nodePath = process.execPath) => {
  const { spawn } = await import('node:child_process');
  return new Promise((ok) => {
    const t0 = Date.now();
    const p = spawn('cscript', ['//nologo', join(ROOT, 'tools', 'launch.vbs'), nodePath],
      { env: { ...process.env, FB_PORT: String(port), FB_NO_OPEN: '1', FB_VBS_TEST: '1' }, stdio: 'pipe' });
    let out = ''; p.stdout.on('data', (d) => { out += d; });
    p.on('exit', () => ok({ out, ms: Date.now() - t0 }));
  });
};

// （H-M16（2026-10-07）段階3b: 試し描きの引っかかり A〜D）
export const guideLike = (page) => page.evaluate(() => {
  document.body.innerHTML = `<div style="max-width:760px;margin:20px auto;font-family:system-ui">
    <h1 style="margin:0 0 20px">タイトル</h1>
    <h2 style="margin:0">1. 登録</h2><div class="box" style="height:80px;border:1px solid #ccc"><p style="margin:10px">登録の説明です。</p></div>
    <h2 style="margin:20px 0 0">2. 使い方</h2><div class="box" style="height:120px;border:1px solid #ccc"><ol style="margin:10px"><li>一つ目の手順</li><li>二つ目の手順</li></ol></div>
    <h2 style="margin:20px 0 0">3. 試す</h2><div class="box" style="height:100px;border:1px solid #ccc"><p style="margin:10px">試す段落。</p></div>
  </div>`;
});

// （H-M16（2026-10-07）段階3b: 試し描きの引っかかり A〜D）
export const rectOfEl = (page, sel) => page.locator(sel).first().boundingBox();

// （H-M14（2026-10-07））
export const runVbsEnv = async (port, extraEnv = {}) => {
  const { spawn } = await import('node:child_process');
  return new Promise((ok) => {
    const t0 = Date.now();
    const p = spawn('cscript', ['//nologo', join(ROOT, 'tools', 'launch.vbs'), process.execPath],
      { env: { ...process.env, FB_PORT: String(port), FB_NO_OPEN: '1', FB_VBS_TEST: '1', ...extraEnv }, stdio: 'pipe' });
    let out = ''; p.stdout.on('data', (d) => { out += d; }); p.stderr.on('data', (d) => { out += d; });
    p.on('exit', (code) => ok({ out, code, ms: Date.now() - t0 }));
  });
};

// （H-M14（2026-10-07））
export const stopAt = (port) => fetch(`http://127.0.0.1:${port}/stop`, { method: 'POST', headers: { 'X-FB-Stop': '1' } }).catch(() => {});

// （H-M07（2026-10-07）: R-18 の列挙外（ページが読み込んでいないもの）を取りに行かない）
export const noStoreServer = async () => {
  const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==', 'base64');
  const srv = createServer((req, res) => {
    res.writeHead(200, { 'content-type': req.url.endsWith('.mp3') ? 'audio/mpeg' : 'image/png', 'access-control-allow-origin': '*', 'cache-control': 'no-store' });
    res.end(png);
  });
  await new Promise((r) => srv.listen(0, '127.0.0.1', r));
  return { srv, other: `http://localhost:${srv.address().port}` };
};

// （verify-h-m18c の F1・N27・N28・N23）
export const sameAsNoCull = async (page, pts) => {
  const shoot = async (noCull) => {
    await page.evaluate((v) => { window.__fbNoCull = v; window.__feedbackTool.clear(); }, noCull);
    await annotate(page, 30, 90, 300, 200, 'x');
    await sendAll(page);
    return pixelsOf(page, pts);
  };
  const ref = await shoot(true);
  const got = await shoot(false);
  return { ref, got };
};

// Playwright テスト: 案内の窓の見張り（T-20、verify-h-m38-b N8。v1 r16 R-23）
// set-window-id.ps1 -WatchPid の付け直し・抜け方は、偽の msedge.exe（test/fake-msedge.cs。画面の外・前面に出さない・タスクバーに出さない）で
// Edge なしに試す。serve.mjs の unref と 2 回目の起動の上限は tools/tag-window.mjs の runTagger で試す
import { test, expect } from '@playwright/test';
import { spawn, spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { ROOT } from './helpers.mjs';
import { runTagger, TAG_ONCE_LIMIT_MS } from '../tools/tag-window.mjs';

const WIN = process.platform === 'win32';
const SCRIPT = join(ROOT, 'tools', 'set-window-id.ps1');
const ICON = join(ROOT, 'win', 'src', 'SnipFeedback', 'app.ico');
const EDGE_ID = 'MSEdge.127.0.0.1_/';   // Edge が 127.0.0.1 の --app 窓に付ける ID
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const rand = () => Math.random().toString(36).slice(2, 8);

let dir, fakeExe;
const started = [];

test.beforeAll(() => {
  if (!WIN) return;
  dir = mkdtempSync(join(tmpdir(), 'fb-fake-edge-'));
  fakeExe = join(dir, 'msedge.exe');
  const q = (t) => `'${t.replace(/'/g, "''")}'`;
  const cmd = `Add-Type -TypeDefinition (Get-Content -Raw -Encoding UTF8 ${q(join(ROOT, 'test', 'fake-msedge.cs'))}) ` +
    `-OutputAssembly ${q(fakeExe)} -OutputType WindowsApplication -ReferencedAssemblies System.Windows.Forms,System.Drawing`;
  const r = spawnSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', Buffer.from(cmd, 'utf16le').toString('base64')], { encoding: 'utf8' });
  if (r.status !== 0) throw new Error('偽の msedge.exe を組み立てられません: ' + r.stderr);
});

test.afterAll(async () => {
  for (const p of started) { try { p.kill(); } catch { /* 終わっている */ } }
  if (dir) { await sleep(500); try { rmSync(dir, { recursive: true, force: true }); } catch { /* 残っても一時フォルダ */ } }
});

// 偽の Edge の窓を開き、get・set を 1 行ずつ尋ねる
async function fakeEdge(title) {
  const p = spawn(fakeExe, [title], { stdio: ['pipe', 'pipe', 'ignore'], windowsHide: true });
  started.push(p);
  const lines = [];
  let waiter = null, buf = '';
  p.stdout.setEncoding('utf8');
  p.stdout.on('data', (d) => {
    buf += d;
    let i;
    while ((i = buf.indexOf('\n')) >= 0) {
      lines.push(buf.slice(0, i).replace(/\r$/, '')); buf = buf.slice(i + 1);
      if (waiter) { const w = waiter; waiter = null; w(); }
    }
  });
  const next = async (ms = 10000) => {
    const until = Date.now() + ms;
    while (!lines.length) {
      if (Date.now() > until) throw new Error('偽の Edge が答えない');
      await new Promise((r) => { waiter = r; setTimeout(r, 200); });
    }
    return lines.shift();
  };
  expect(await next()).toBe('ready');
  const ask = async (cmd) => { p.stdin.write(cmd + '\n'); return next(); };
  return { p, ask };
}

// set-window-id.ps1 を動かす。出力と終わり方を受け取れる（出力は UTF-8 にして読む。既定は CP932）
function watcher(args) {
  const q = (t) => `'${String(t).replace(/'/g, "''")}'`;
  const cmd = `[Console]::OutputEncoding = [Text.Encoding]::UTF8; & ${q(SCRIPT)} ${args.map((a) => (a.startsWith('-') ? a : q(a))).join(' ')}; exit $LASTEXITCODE`;
  const p = spawn('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', Buffer.from(cmd, 'utf16le').toString('base64')], { stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true });   // コンソールの無い親から動かしても窓を出さない（verify T-20 N4）
  started.push(p);
  let out = '';
  p.stdout.setEncoding('utf8'); p.stdout.on('data', (d) => { out += d; });
  p.stderr.setEncoding('utf8'); p.stderr.on('data', (d) => { out += d; });
  const exited = new Promise((r) => p.on('exit', (code) => r(code)));
  return { p, exited, out: () => out };
}

async function until(fn, ms) {
  const end = Date.now() + ms;
  while (Date.now() < end) { if (await fn()) return true; await sleep(250); }
  return false;
}

const within = (promise, ms) => Promise.race([promise, sleep(ms).then(() => 'timeout')]);

test('T-20: 見張り（-WatchPid）は案内の窓に ID を付け、Edge が付け直しても付け直し、見張る相手が終わると exit 0 で抜ける', async () => {
  test.skip(!WIN, 'Windows だけ');
  test.setTimeout(60000);
  const title = `FbTagTest-${rand()}`, appId = `FeedbackSystem.Test.${rand()}`;
  const edge = await fakeEdge(title);
  const server = spawn(process.execPath, ['-e', 'setTimeout(() => {}, 600000)'], { stdio: 'ignore' });   // 配布サーバの代わり
  started.push(server);
  const w = watcher(['-Title', title, '-AppId', appId, '-Icon', ICON, '-Command', 'x', '-WatchPid', String(server.pid)]);

  expect(await until(async () => (await edge.ask('get')) === appId, 20000)).toBe(true);
  // Edge が ID を戻した（再読み込みなど）のと同じ状態にする → 見張りが 2 秒ごとに付け直す
  expect(await edge.ask(`set ${EDGE_ID}`)).toBe('ok');
  expect(await edge.ask('get')).toBe(EDGE_ID);
  expect(await until(async () => (await edge.ask('get')) === appId, 8000)).toBe(true);
  expect(w.p.exitCode).toBeNull();   // 見張る相手が動いている間は抜けない

  server.kill();
  expect(await within(w.exited, 10000)).toBe(0);
  expect(w.out()).toContain('付けた');
});

test('T-20: 見張りなし（2 回目の起動）は 1 回付けて -HoldSeconds の後に exit 0、窓が無ければ -WaitSeconds の後に exit 1', async () => {
  test.skip(!WIN, 'Windows だけ');
  test.setTimeout(60000);
  const title = `FbTagTest-${rand()}`, appId = `FeedbackSystem.Test.${rand()}`;
  const edge = await fakeEdge(title);
  const once = watcher(['-Title', title, '-AppId', appId, '-Icon', ICON, '-Command', 'x', '-HoldSeconds', '1']);
  expect(await within(once.exited, 20000)).toBe(0);
  expect(await edge.ask('get')).toBe(appId);

  const none = watcher(['-Title', `FbTagTest-none-${rand()}`, '-AppId', appId, '-Icon', ICON, '-Command', 'x', '-WaitSeconds', '1']);
  expect(await within(none.exited, 20000)).toBe(1);
  expect(none.out()).toContain('窓が見つからない');
});

test('T-20: 通常のタブの題の窓（「 - Microsoft Edge」で終わる）には ID を付けない', async () => {
  test.skip(!WIN, 'Windows だけ');
  test.setTimeout(60000);
  const title = `FbTagTest-${rand()}`, appId = `FeedbackSystem.Test.${rand()}`;
  const tab = await fakeEdge(`${title} - Microsoft​ Edge`);
  const before = await tab.ask('get');
  const once = watcher(['-Title', title, '-AppId', appId, '-Icon', ICON, '-Command', 'x', '-WaitSeconds', '1']);
  expect(await within(once.exited, 20000)).toBe(1);
  expect(await tab.ask('get')).toBe(before);
});

const HANG = ['-e', 'setTimeout(() => {}, 600000)'];

test('T-20: runTagger（2 回目の起動）は付け終わるのを待ち、終わらなければ上限で止めて抜ける。既定の上限は 25 秒', async () => {
  expect(TAG_ONCE_LIMIT_MS).toBe(25000);
  let t = Date.now();
  await runTagger(process.execPath, ['-e', ''], { watch: false, limitMs: 20000 });
  expect(Date.now() - t).toBeLessThan(10000);   // 子が終わればすぐ

  t = Date.now();
  const p = await within(runTagger(process.execPath, HANG, { watch: false, limitMs: 500 }), 10000);
  expect(p).not.toBe('timeout');
  expect(Date.now() - t).toBeLessThan(5000);
  expect(await until(() => p.exitCode !== null || p.signalCode !== null, 5000)).toBe(true);   // 上限で子を止めた
});

test('T-20: runTagger（見張り）はすぐ解決し、unref するので node は見張りを待たずに終われる', async () => {
  const url = pathToFileURL(join(ROOT, 'tools', 'tag-window.mjs')).href;
  const code = `import { runTagger } from ${JSON.stringify(url)};` +
    `const p = await runTagger(process.execPath, ${JSON.stringify(HANG)}, { watch: true }); process.stdout.write(String(p.pid));`;   // console.log は色が付く（FORCE_COLOR）
  const node = spawn(process.execPath, ['--input-type=module', '-e', code], { stdio: ['ignore', 'pipe', 'inherit'] });
  let out = ''; node.stdout.on('data', (d) => { out += d; });
  const code0 = await within(new Promise((r) => node.on('close', r)), 10000);   // close: 出力を読み終えてから
  const pid = Number(out.trim());
  try { if (pid) process.kill(pid); } catch { /* 終わっている */ }
  if (code0 === 'timeout') node.kill();
  expect(code0).toBe(0);
  expect(pid).toBeGreaterThan(0);
});

test('T-20: serve.mjs は tag-window.mjs の runTagger で見張りを起動する', async () => {
  const src = readFileSync(join(ROOT, 'serve.mjs'), 'utf8');
  expect(src).toContain("import { runTagger } from './tools/tag-window.mjs'");
  expect(src).toContain('return runTagger(ps, args, { watch });');
  expect(src).toMatch(/watch \? \['-WatchPid', String\(process\.pid\)\] : \[\]/);
  // 初回は見張り、2 回目（ALREADY）は 1 回付け終わるのを待ってから抜ける（verify T-20 N1）
  expect(src).toContain('if (LAUNCH) openPage(true);');
  expect(src).toMatch(/report\('ALREADY'\);[\s\S]{0,120}await openPage\(false\);\s*process\.exit\(0\);/);
  // 既定の上限は TAG_ONCE_LIMIT_MS（verify T-20 N3）
  expect(readFileSync(join(ROOT, 'tools', 'tag-window.mjs'), 'utf8')).toContain('{ watch, limitMs = TAG_ONCE_LIMIT_MS } = {}');
});

test('T-20: runTagger は起動できないコマンドでも落ちない（見張りはすぐ解決、1 回は error で解決）', async () => {
  const missing = join(ROOT, 'no-such-dir', 'no-such.exe');   // verify T-20 N2
  await expect(within(runTagger(missing, [], { watch: true }), 5000)).resolves.not.toBe('timeout');
  await expect(within(runTagger(missing, [], { watch: false, limitMs: 20000 }), 5000)).resolves.not.toBe('timeout');
  await sleep(300);   // 見張りの error が後から来ても、ここまで落ちなければよい
});

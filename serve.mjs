// 配布サーバ — dist/feedback.js を 127.0.0.1 だけで配る（ブックマークレット用）。案内ページ付き
// 使い方:
//   npm run serve    … 前面で起動（Ctrl+C で停止）
//   npm run launch   … ショートカット用。動いていなければ起動し、案内ページをブラウザで開く。動いていれば開くだけ
// 環境変数: FB_PORT（既定 8799）、FB_NO_OPEN=1（ブラウザを開かず URL を表示するだけ。テスト用）
import { createServer } from 'node:http';
import { readFileSync, existsSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';
import { runTagger } from './tools/tag-window.mjs';

const ROOT = dirname(fileURLToPath(import.meta.url));
const PORT = Number(process.env.FB_PORT || 8799);
const LAUNCH = process.argv.includes('--launch');
const file = join(ROOT, 'dist', 'feedback.js');
const { version } = JSON.parse(readFileSync(join(ROOT, 'package.json'), 'utf8'));
const pageUrl = `http://127.0.0.1:${PORT}/`;
// アイコンは v2（画面切り取りフィードバック）と同じもの（H-M38、r16 R-23）。ショートカット・タブ・タスクバーで使う
const ICON = join(ROOT, 'win', 'src', 'SnipFeedback', 'app.ico');
const TITLE = '画面赤枠フィードバック';

// 案内ページを独立したウィンドウで開く（H-M22）: Edge のアプリウィンドウ（agent-cockpit と同じ）。無ければ既定のブラウザ
function edgePath() {
  const pf86 = process.env['ProgramFiles(x86)'] || 'C:\\Program Files (x86)', pf = process.env.ProgramFiles || 'C:\\Program Files';
  return [join(pf86, 'Microsoft', 'Edge', 'Application', 'msedge.exe'), join(pf, 'Microsoft', 'Edge', 'Application', 'msedge.exe')].find((p) => existsSync(p));
}
// watch: true は配布サーバが動いている間ずっと案内の窓を見張る（初回の起動）。false は 1 回付けて終わる（2 回目の起動）。
// 戻り値: 1 回付け終わる（または付けない）と解決する Promise（見張るときはすぐ解決）
function openPage(watch) {
  if (process.env.FB_NO_OPEN === '1') { console.log(`開く: ${pageUrl}`); return Promise.resolve(); }
  const edge = edgePath();
  if (edge) {
    spawn(edge, [`--app=${pageUrl}`, '--window-size=640,820'], { detached: true, stdio: 'ignore', windowsHide: true }).unref();
    return tagWindow(watch);
  }
  spawn('cmd', ['/c', 'start', '""', pageUrl], { detached: true, stdio: 'ignore', windowsHide: true }).unref();
  return Promise.resolve();
}

// 案内の窓に専用の AppUserModelID とアイコンを付ける（H-M38、r16 R-23）。Edge は 127.0.0.1 の --app 窓すべてに
// 同じ ID（MSEdge.127.0.0.1_/）を付けるので、そのままだとタスクバーでスライド赤枠レビューなどと同じボタン・同じアイコンになる。
// 初回の起動では、配布サーバが動いている間ずっと見張る（2 回目の起動で開いた窓や、Edge が ID を付け直した窓にも付け直す。verify-h-m38 N1）。
// 付けられなくても案内の窓はそのまま使える。起動と待ち方は tools/tag-window.mjs（2 回目の起動では最長 25 秒待つ）
function tagWindow(watch) {
  const sys32 = join(process.env.SystemRoot || 'C:\\Windows', 'System32');
  const ps = join(sys32, 'WindowsPowerShell', 'v1.0', 'powershell.exe');
  const relaunch = `"${join(sys32, 'wscript.exe')}" "${join(ROOT, 'tools', 'launch.vbs')}" "${process.execPath}"`;   // ショートカットと同じ起動
  const args = ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', join(ROOT, 'tools', 'set-window-id.ps1'),
    '-Title', TITLE, '-Icon', ICON, '-Command', relaunch, ...(watch ? ['-WatchPid', String(process.pid)] : [])];
  return runTagger(ps, args, { watch });
}

// --launch のとき、開いている案内ページ（/events の接続）がすべて閉じたら配布サーバを止める（H-M22）。
// 再読み込みでいったん切れても止まらないよう、少し待ってから判断する。一度も開かれていなければ止めない
const STOP_GRACE_MS = 3000;
const watchers = new Set();
let everWatched = false, stopTimer = null;
function watcherClosed() {
  if (!LAUNCH || watchers.size > 0) return;
  clearTimeout(stopTimer);
  stopTimer = setTimeout(() => {
    if (watchers.size > 0) return;
    console.log('案内ページがすべて閉じられたので停止します');
    server.close(); setTimeout(() => process.exit(0), 100);
  }, STOP_GRACE_MS);
}

function bookmarklet() {
  try {
    return readFileSync(join(ROOT, 'docs', 'bookmarklet.txt'), 'utf8').trim().replace('127.0.0.1:8799', `127.0.0.1:${PORT}`);
  } catch { return null; }   // 無くても案内ページは出す
}

// 起動の結果を一時ファイルに書く。ショートカット起動では tools/launch.vbs がこれを読んで、原因に合った知らせを出す（H-M13）
// 内容: OK / ALREADY / PORT_IN_USE / ERROR <理由>
const statusFile = join(tmpdir(), `feedback-system-launch-${process.env.FB_PORT || 8799}.txt`);
function report(status) {
  if (!LAUNCH) return;
  try { writeFileSync(statusFile, status, 'utf8'); } catch { /* 書けなくても起動は続ける */ }
}
function notify(msg) { console.error(msg); }
process.on('uncaughtException', (e) => {   // 起動中の想定外の失敗（範囲外のポート等）
  report('ERROR ' + (e && e.message || e));
  console.error(e); process.exit(1);
});

const esc = (s) => s.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');

function guidePage(counted = true) {
  const bm = bookmarklet();
  // 閉じたら止まるのは --launch で、このページが数えられているときだけ（verify-h-m26 N52）
  const win = LAUNCH && counted;
  const bmHtml = bm ? `この <a id="bm" href="${esc(bm)}">赤枠</a> をブックマークバーへドラッグする（別のウィンドウへドラッグできないときは <button id="copy-bm" type="button">コピー</button> して、ブックマークの URL 欄に貼る）。<span id="copied"></span>`
    : 'docs/bookmarklet.txt が読めないため、登録用のリンクを出せません。リポジトリの docs/bookmarklet.txt を確かめてください。';
  return `<!doctype html>
<html lang="ja"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>画面赤枠フィードバック</title>
<link rel="icon" href="/favicon.ico">
<style>
  body{font-family:system-ui,sans-serif;max-width:760px;margin:32px auto;padding:0 16px;line-height:1.7;color:#222;background:#fafafa}
  h1{font-size:20px;margin-bottom:4px} .ver{color:#888;font-size:12px}
  h2{font-size:16px;margin-top:28px;border-left:4px solid #e53935;padding-left:8px}
  .box{background:#fff;border:1px solid #ddd;border-radius:8px;padding:16px;margin-top:10px}
  a#bm{display:inline-block;padding:6px 14px;background:#e53935;color:#fff;border-radius:6px;text-decoration:none;font-weight:bold;cursor:grab}
  ol{margin:6px 0 0 0} .ok{color:#2e7d32;font-weight:bold} .note{margin:10px 0 0;color:#555}
  summary{cursor:pointer;color:#555}
  .try{background:#f3f6fb} button#stop{padding:6px 12px;border:1px solid #bbb;border-radius:4px;background:#f6f6f6;cursor:pointer}
</style></head>
<body>
<h1>画面赤枠フィードバック</h1>
<div class="ver">feedback.js v${version} ・ <span class="ok">配信中</span>（http://127.0.0.1:${PORT}/feedback.js）${win ? ' ・ このウィンドウを閉じると配信を止めます' : ''}</div>

<h2>1. 使い方</h2>
<div class="box">
  <ol>
    <li id="step-open">普段の Chrome で指摘したい localhost の画面を開き、ブックマークバーの「赤枠」を押す → 右下に「枠」「送信」が出る（${win ? 'このウィンドウにはブックマークバーが無い。' : ''}未登録なら下の「4.」で登録）</li>
    <li>Alt＋ドラッグで赤枠（Alt が効かなければ右下「枠」を押してからドラッグ）</li>
    <li>一言を入れて Enter（複数あれば繰り返す）</li>
    <li>Ctrl＋Enter → 画像とテキストがコピーされる</li>
    <li>Claude の Code タブの入力欄で Ctrl＋V（両方入る）</li>
  </ol>
  ${win ? '<p id="keep-open" class="note">このウィンドウは使い終わるまで開いておく（閉じると配信が止まり「赤枠」が効かなくなる。次はショートカットから起動）。</p>' : ''}
</div>

<h2>2. ここで試す</h2>
<div class="box try">
  <p>この枠の中を Alt＋ドラッグして、一言 → Enter → Ctrl＋Enter。</p>
  <p>サンプルの段落。ここを赤枠で囲んでみてください。</p>
</div>

<h2>3. 停止</h2>
<div class="box">
  使い終わったら ${win ? 'このウィンドウを閉じるか、' : ''}<button id="stop" type="button">配信を止める</button>（次はショートカットから起動）
  <span id="stopped"></span>
</div>

<h2>4. ブックマークに登録（最初の1回だけ）</h2>
<details id="register" class="box">
  <summary>登録済みなら不要。初めて使うときだけ開く</summary>
  <p>${bmHtml}</p>
  <p>普段使う Chrome のブックマークバーに入れる。ツールを更新しても登録し直す必要はない。</p>
</details>
<script>
  // H-M22: このページが開いている間は配信を続け、閉じたら止める（--launch のとき）
  ${counted ? "try { new EventSource('/events'); } catch (e) {}" : '/* よそのページから開かれたので数えない */'}
  var cb = document.getElementById('copy-bm');
  if (cb) cb.onclick = function () {
    navigator.clipboard.writeText(document.getElementById('bm').getAttribute('href')).then(function () {
      document.getElementById('copied').textContent = 'コピーしました';
    }, function () {
      document.getElementById('copied').textContent = 'コピーできませんでした。「赤枠」を右クリックして「リンクのアドレスをコピー」してください';
    });
  };
  document.getElementById('stop').onclick = function () {
    fetch('/stop', { method: 'POST', headers: { 'X-FB-Stop': '1' } }).then(function () {
      document.getElementById('stopped').textContent = '止めました。このタブは閉じてかまいません。';
    });
  };
</script>
<script src="/feedback.js"></script>
</body></html>`;
}

const ALLOWED_HOSTS = [`127.0.0.1:${PORT}`, `localhost:${PORT}`];

const server = createServer((req, res) => {
  const path = (req.url || '/').split('?')[0];
  // DNS リバインディング対策: よその名前（Host）で来たリクエストは断る（R-21: よそのページからは止められない）
  if (!ALLOWED_HOSTS.includes(String(req.headers.host || '').toLowerCase())) { res.writeHead(403); return res.end(); }
  if (req.method === 'GET' && path === '/feedback.js') {
    if (!existsSync(file)) { res.writeHead(500); return res.end('dist/feedback.js がありません。node build.mjs を先に実行してください'); }
    // CORS の許可は付けない（script タグでの読み込みには要らない。よそのサイトからツールの有無・版・中身を読ませない。R-21 r17）
    res.writeHead(200, { 'content-type': 'text/javascript; charset=utf-8', 'cache-control': 'no-store' });
    return res.end(readFileSync(file)); // 毎回読み直すので、ビルドし直せば再読み込みだけで反映される
  }
  if (req.method === 'GET' && path === '/') {
    // よそのページから開かれた案内ページ（window.open 等）は「開いている案内ページ」として数えない。
    // Edge のアプリウィンドウ・アドレス欄・再読み込みは none／same-origin。iframe には入れさせない（verify-h-m22b F2'）
    const site = req.headers['sec-fetch-site'];
    const counted = !site || site === 'none' || site === 'same-origin';
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store',
      'content-security-policy': "frame-ancestors 'none'", 'x-frame-options': 'DENY' });
    return res.end(guidePage(counted));
  }
  if (req.method === 'GET' && path === '/events') {
    // 案内ページ自身（同じオリジン）からの接続だけを数える。よそのページが開いて閉じるだけで止められないように（verify-h-m22 F2）。
    // Sec-Fetch-Site はブラウザが付ける。付かないのはブラウザ以外（テスト・curl）で、よそのページではない
    const site = req.headers['sec-fetch-site'];
    if (site && site !== 'same-origin') { res.writeHead(403); return res.end(); }
    res.writeHead(200, { 'content-type': 'text/event-stream', 'cache-control': 'no-store', connection: 'keep-alive' });
    res.write(': open\n\n');
    watchers.add(res); everWatched = true; clearTimeout(stopTimer);
    const ping = setInterval(() => res.write(': ping\n\n'), 15000);   // 経路で切られないように
    req.on('close', () => { clearInterval(ping); watchers.delete(res); if (everWatched) watcherClosed(); });
    return;
  }
  if (req.method === 'GET' && path === '/favicon.ico') {
    if (!existsSync(ICON)) { res.writeHead(404); return res.end(); }
    res.writeHead(200, { 'content-type': 'image/x-icon', 'cache-control': 'no-cache' });
    return res.end(readFileSync(ICON));
  }
  if (req.method === 'GET' && path === '/health') {
    res.writeHead(200, { 'content-type': 'application/json' });
    return res.end(JSON.stringify({ app: 'feedback-system', version }));
  }
  if (req.method === 'POST' && path === '/stop') {
    // 合図のヘッダ必須。よそのページからは CORS の事前確認で止まるので送れない（任意のサイトから止められないように）
    if (req.headers['x-fb-stop'] !== '1') { res.writeHead(403); return res.end(); }
    res.writeHead(200); res.end('stopped');
    console.log('停止しました');
    server.close(); setTimeout(() => process.exit(0), 100);
    return;
  }
  res.writeHead(404); res.end();
});

async function alreadyRunning() {
  try {
    const r = await fetch(`http://127.0.0.1:${PORT}/health`, { signal: AbortSignal.timeout(1500) });
    return r.ok && (await r.json()).app === 'feedback-system';
  } catch { return false; }
}

server.on('error', async (e) => {
  if (e.code !== 'EADDRINUSE') { report('ERROR ' + e.message); notify(`起動できません: ${e.message}`); setTimeout(() => process.exit(1), 300); return; }
  if (LAUNCH && await alreadyRunning()) {
    report('ALREADY');
    console.log(`起動済みのサーバを使います: ${pageUrl}`);
    await openPage(false);
    process.exit(0);
  }
  report('PORT_IN_USE');
  notify(`ポート ${PORT} を別のアプリ（または古い版の配布サーバ）が使っているため起動できません。そのアプリを止めるか、FB_PORT で別のポートを指定してください`);
  setTimeout(() => process.exit(1), 300);
});

server.listen(PORT, '127.0.0.1', () => {
  console.log(`feedback.js を http://127.0.0.1:${PORT}/feedback.js で配信中（Ctrl+C で停止）`);
  report('OK');
  if (LAUNCH) openPage(true);
});

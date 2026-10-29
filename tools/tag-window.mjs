// 案内の窓に ID を付ける set-window-id.ps1 の起動と待ち方（H-M38。T-20 で serve.mjs から出して試せる形にした）
// detached にすると PowerShell がコンソール無しで起動し、付けられない（2026-10-20 に確かめた）。切り離さずに隠して起動するので、
// node が終わると一緒に終わる。
//   watch: true  … 配布サーバが動いている間ずっと見張る。待たずにすぐ解決し、unref して配布サーバの終わりを妨げない
//   watch: false … 2 回目の起動（ALREADY）用。1 回付け終わるのを待ってから解決する（最長 limitMs。verify-h-m38 N3）
import { spawn } from 'node:child_process';

export const TAG_ONCE_LIMIT_MS = 25000;

// 戻り値: 解決する Promise（見張るときはすぐ。1 回のときは子が終わるか上限で子を止めたとき）
export function runTagger(cmd, args, { watch, limitMs = TAG_ONCE_LIMIT_MS } = {}) {
  const p = spawn(cmd, args, { stdio: 'ignore', windowsHide: true });
  if (watch) { p.on('error', () => {}); p.unref(); return Promise.resolve(p); }   // 配布サーバの終わりを妨げない
  return new Promise((done) => {
    const limit = setTimeout(() => { p.kill(); done(p); }, limitMs);
    const end = () => { clearTimeout(limit); done(p); };
    p.on('error', end); p.on('exit', end);   // unref しない（待っている間に node が抜けないように）
  });
}

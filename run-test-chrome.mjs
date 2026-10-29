// npm run test:chrome — インストール済み Google Chrome でテスト（cross-env 不要、Windows 可）。引数は playwright test へ渡す
// shell を使わず node で直接起動する（Node 24 の DEP0190 を避ける）
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
const env = { ...process.env, PW_CHANNEL: 'chrome' };
const run = (args) => { const r = spawnSync(process.execPath, args, { stdio: 'inherit', env }); if (r.status !== 0) process.exit(r.status ?? 1); };
run([fileURLToPath(new URL('build.mjs', import.meta.url))]);
run([fileURLToPath(new URL('node_modules/@playwright/test/cli.js', import.meta.url)), 'test', ...process.argv.slice(2)]);

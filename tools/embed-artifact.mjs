// アーティファクト用: HTML ファイルの </body> 直前（無ければ末尾）に feedback.js をインラインで埋め込む（R-11）
// 使い方: node tools/embed-artifact.mjs <入力.html> [出力.html]   ※出力を省くと上書き
// 二重埋め込みは「feedback-tool:」の印で検出してスキップする
import { readFileSync, writeFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const [input, output = input] = process.argv.slice(2);
if (!input) { console.error('usage: node tools/embed-artifact.mjs <in.html> [out.html]'); process.exit(2); }
const html = readFileSync(input, 'utf8');
if (html.includes('<!-- feedback-tool:')) { console.log('既に埋め込み済みのため変更なし:', input); process.exit(0); }
const block = readFileSync(join(ROOT, 'dist', 'feedback.inline.html'), 'utf8');
const i = html.toLowerCase().lastIndexOf('</body>');
const out = i >= 0 ? html.slice(0, i) + block + html.slice(i) : html + '\n' + block;
writeFileSync(output, out);
console.log(`埋め込みました: ${output}（+${(Buffer.byteLength(block) / 1024).toFixed(1)} KB）`);

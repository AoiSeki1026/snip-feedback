// dist/feedback.js（1行読み込み用）と dist/feedback.inline.html（アーティファクト埋め込み用）を生成する
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';

mkdirSync('dist', { recursive: true });
const h2c = readFileSync('node_modules/html2canvas/dist/html2canvas.min.js', 'utf8');
const { version } = JSON.parse(readFileSync('package.json', 'utf8'));
const src = readFileSync('src/feedback.js', 'utf8').replace("'__FB_VERSION__'", JSON.stringify(version));
const banner = `/* feedback.js v${version} — 視覚フィードバック入力ツール（html2canvas 1.4.1 同梱, MIT） */\n`;
// 改行は LF に揃える（Windows で src が CRLF で取り出されていても、dist の中身が変わらないように。.gitattributes は dist を LF にする）
const bundle = (banner + h2c + '\n;\n' + src).replace(/\r\n/g, '\n');
writeFileSync('dist/feedback.js', bundle);

const inline = `<!-- feedback-tool: Alt+ドラッグで赤枠 → 一言 → Enter → Ctrl+Enter。このブロックを </body> の直前に貼る -->\n<script>\n${bundle.replace(/<\/script/gi, '<\\/script')}\n</script>\n`;
writeFileSync('dist/feedback.inline.html', inline);

const kb = (n) => (n / 1024).toFixed(1) + ' KB';
console.log('dist/feedback.js', kb(Buffer.byteLength(bundle)), '| dist/feedback.inline.html', kb(Buffer.byteLength(inline)));

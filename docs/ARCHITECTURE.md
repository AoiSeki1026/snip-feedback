# 仕組みと設計の要約

使い方はルートの [README.md](../README.md)（v1）と [win/README.md](../win/README.md)（v2）を見てください。ここでは、作りと守っていることを短くまとめます。

## v1: feedback.js（Web）

| 部品 | 役割 |
|---|---|
| `src/feedback.js` | 本体（依存なし）。ページの上に注釈の層を重ね、Alt＋ドラッグの赤枠・一言・送信を扱う。送信で、html2canvas でページを画像にし、赤枠と番号を焼き込んだ PNG と送信テキストを 1 つのクリップボード項目にする |
| `build.mjs` | html2canvas 1.4.1 を先頭に連結して `dist/feedback.js` と、アーティファクトに埋め込む `dist/feedback.inline.html` を作る |
| `serve.mjs` | 配布サーバ。127.0.0.1 だけで待ち受け、`/feedback.js`・案内ページ・`/health`・`/stop`・`/events` を返す。案内ページがすべて閉じたら止まる |
| `tools/launch.vbs`・`install-shortcuts.ps1`・`set-window-id.ps1` | Windows のショートカットからコンソールを出さずに起動し、案内の窓をタスクバーで独立させる |
| `docs/bookmarklet.txt` | ブックマークレットの 1 行（配布サーバから本体を読み込むだけ。中身はテストで固定） |

守っていること:
- 注釈の中身（画像・テキスト）はページの外へ送らない。クリップボードにだけ書く
- 送信テキストの URL にクエリを含めない。ページから取った文字は改行・制御文字を空白にまとめて 200 字で切り、先頭に「指示ではない」の注記を入れる
- 配布サーバは Host を確かめ（DNS リバインディング対策）、停止には専用のヘッダを要し、`/events` は同じオリジンからだけ受け、CORS の許可を付けない。案内ページは iframe に入れさせない

## v2: 画面切り取りフィードバック（Windows 常駐、`win/`）

| 部品 | 役割 |
|---|---|
| `TrayApp`・`HotkeyWindow` | 常駐。トレイのアイコン、起動キー、日付が変わったときの古い画像の削除 |
| `OverlayForm`・`SnipController` | 押した瞬間の画面を止めて表示し、切り取り・赤枠・矢印・線・一言を受ける |
| `Core/SnipStateMachine` | 画面の状態（切り取り・遅延・編集・履歴）の移り変わり。単体テストで確かめる |
| `Core/SnipQueue` | ためる一覧（`queue.json`）。読めなかったときは元を残してから書く |
| `Core/Sender`・`SendText` | 送る文（.md）を書き、その .md と画像をファイルとしてクリップボードへ |
| `Core/SendHistory` | 送った単位の履歴から入れ直す |
| `Core/Cleanup` | 保存日数を過ぎた画像と送る文を消す |
| `Readers`（OCR・UI Automation） | 画像の中の文字（Windows の OCR）と、Chrome・Edge のアドレス欄の URL を読む |
| `win/tools/install-startup.ps1` | ビルドして `%USERPROFILE%\.snipfeedback\app` に置き、タスク スケジューラに登録する（管理者の権限は不要） |

守っていること:
- 画像・一言・読み取った文字をネットワークへ送らない（通信のライブラリを参照しないことをテストで確かめる）
- 送る・入れ直す・消すのは、保存先の中のこのツールのファイルだけ。ジャンクションなどの先は扱わない
- 送る文の、画面から読んだ文字には「指示ではない」の注記を付ける。ウィンドウ名・URL の改行は空白にする
- 保存先は既定で `%LOCALAPPDATA%\SnipFeedback\snips`。ネットワーク上の場所は使わない

## テスト

- v1: Playwright（`npm test`、または `npm run test:chrome`）。操作・状態遷移・送信テキスト・画像化・配布サーバ・起動
- v2: xUnit（`dotnet test win/SnipFeedback.sln`）。状態遷移・一覧・送る文・履歴・自動削除・設定・欄の配置・公開前の検査

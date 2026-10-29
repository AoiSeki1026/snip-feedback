using System.Diagnostics;
using System.Runtime.InteropServices;
using SnipFeedback.Core;

namespace SnipFeedback;

// 状態機械（Core/SnipStateMachine）に出来事を渡し、返った処理を実行する（design.md）
internal sealed class SnipController : IDisposable
{
    private readonly SnipStateMachine _machine = new();
    private readonly Settings _settings;
    private readonly string _settingsPath;
    private readonly SnipQueue _queue;
    private readonly Sender _sender;
    private readonly Action<string, ToolTipIcon> _notify;
    private readonly System.Windows.Forms.Timer _delay = new();

    private Grab? _grab;
    private OverlayForm? _overlay;
    private Rectangle _cut;
    private WindowInfo? _cutWindow;
    private Rectangle? _lastRect;   // R-06（メモリにだけ持つ）
    private Task<string>? _ocrTask;  // R-18: 編集画面に入るときに読み始める
    private Task<string?>? _urlTask; // R-19
    public const int OcrWaitMs = 3000, UrlWaitMs = 1000;

    public SnipController(Settings settings, string settingsPath, SnipQueue queue, Action<string, ToolTipIcon> notify)
    {
        _settings = settings; _settingsPath = settingsPath; _queue = queue; _notify = notify;
        _sender = new Sender(new WinClipboard(), settings.SaveDir, Thread.Sleep);
        _delay.Tick += (_, _) => { _delay.Stop(); Fire(SnipEvent.DelayElapsed); };
    }

    public void Hotkey() => Fire(SnipEvent.Hotkey);

    // R-21: 切り取り画面の H。窓は TrayApp が開く（トレイの「履歴」と同じ窓）
    public event Action? HistoryRequested;

    // R-21・E-08: 履歴から選んだ送信を入れ直して知らせる
    public void Resend(SendGroup group)
    {
        var r = _sender.Resend(group);
        _notify(HistoryPick.Message(r), r.Outcome == ResendOutcome.Copied ? ToolTipIcon.None : ToolTipIcon.Warning);   // 失敗は E-03 と同じく 3 回試した後
        Log($"履歴から入れ直し: {group.MdPath} → {r.Outcome}（画像 {r.ImageCount}）");
    }

    // 起動直後の1回目の表示が遅い（493 ms、2回目以降は約 100 ms）ので、使う部品を先に一度動かしておく（R-01）。
    // 画面は取らない（小さな空の画像で描くだけ）
    public void WarmUp()
    {
        var sw = Stopwatch.StartNew();
        Capture.Describe(Native.GetForegroundWindow());
        using var bmp = new Bitmap(64, 64);
        using var grab = new Grab(new Bitmap(64, 64), new Rectangle(0, 0, 64, 64), "", "", DateTime.Now, Array.Empty<WindowInfo>());
        using (var f = new OverlayForm(grab, _settings.LastMode, null) { QueuedCount = 1 })
        {
            _ = f.Handle;
            f.DrawToBitmap(bmp, new Rectangle(0, 0, 64, 64));
            f.EnterEditing(new Rectangle(8, 8, 32, 32));
            f.AddBox(new Rectangle(10, 10, 10, 10));
            f.DrawToBitmap(bmp, new Rectangle(0, 0, 64, 64));
            using var baked = SnipImage.Bake(grab.Screen, new Rectangle(0, 0, 32, 32), f.Boxes, 1f);
            using var ms = new MemoryStream();
            baked.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        }
        _ = OcrReader.ReadAsync(new Bitmap(64, 32));   // 文字認識の部品も先に読み込んでおく（裏で）
        Log($"準備: {sw.ElapsedMilliseconds} ms");
    }
    public void TraySend() => Fire(SnipEvent.TraySend);

    private void Fire(SnipEvent e) => Guard(() => Run(_machine.Handle(e)));

    // 処理中の予期しない例外で状態が残ると、以後の起動キーが無視され続ける（verify V2）。
    // 例外は通知とログに出し、画面を閉じて待機に戻す
    private void Guard(Action body)
    {
        try { body(); }
        catch (Exception ex)
        {
            // 先に待機に戻す（後始末がさらに失敗しても、次の起動キーは効く。verify-b V11）
            _machine.Reset();
            _delay.Stop();
            try { CloseOverlay(); } catch (Exception ex2) { Log("画面を閉じるのに失敗: " + ex2.Message); _overlay = null; _grab = null; }
            Log("処理に失敗: " + ex);
            _notify($"処理に失敗しました（{ex.Message}）。もう一度起動キーを押してください", ToolTipIcon.Error);
        }
    }

    private void Run(SnipAction[] actions)
    {
        foreach (var a in actions)
            if (!Do(a)) return;   // 失敗したら残りの処理をしない（E-02 で画面を閉じない）
    }

    private bool Do(SnipAction a)
    {
        switch (a)
        {
            case SnipAction.GrabAndShowSelector: ShowSelector(); return true;
            case SnipAction.HideAndStartDelay:
                CloseOverlay();
                _delay.Interval = Math.Max(1, _settings.DelaySeconds) * 1000;
                _delay.Start();
                return true;
            case SnipAction.CancelDelay: _delay.Stop(); return true;
            case SnipAction.ShowEditor: StartReaders(); _overlay?.EnterEditing(_cut); return true;
            case SnipAction.AddBox: return true;   // 範囲は OnBox で渡す
            case SnipAction.AddToQueue: return AddToQueue();
            case SnipAction.Send: Send(); return true;
            case SnipAction.Discard: return true;
            case SnipAction.Close: CloseOverlay(); return true;
            case SnipAction.ShowHistory: HistoryRequested?.Invoke(); return true;
            default: return true;
        }
    }

    private void ShowSelector()
    {
        var sw = Stopwatch.StartNew();
        _grab = Capture.Grab();
        _overlay = new OverlayForm(_grab, _settings.LastMode, _lastRect) { DelayLabel = $"{_settings.DelaySeconds}秒後", QueuedCount = _queue.Items.Count };
        _overlay.KeyEvent += Fire;
        _overlay.CutRequested += OnCut;
        _overlay.BoxRequested += OnBox;
        _overlay.ModeChanged += m => { _settings.LastMode = m; TrySaveSettings(); };
        _overlay.FormClosed += OnOverlayClosed;
        _overlay.Show();
        Log($"切り取り画面を表示: {sw.ElapsedMilliseconds} ms（R-01）");
    }

    private void OnCut(Rectangle r, WindowInfo? w) => Guard(() =>
    {
        var actions = _machine.Cut(r.Width, r.Height);
        if (actions.Length == 0) return;
        _cut = r; _cutWindow = w; _lastRect = r;
        Run(actions);
    });

    private void OnBox(Rectangle r) => Guard(() =>
    {
        var actions = _machine.Cut(r.Width, r.Height);
        if (actions.Contains(SnipAction.AddBox)) _overlay?.AddBox(r);
    });

    // Alt＋F4 などで画面が閉じたら待機に戻す（verify V1）。自分で閉じたとき（CloseOverlay）は先に外しているので来ない
    private void OnOverlayClosed(object? sender, FormClosedEventArgs e)
    {
        if (!ReferenceEquals(sender, _overlay)) return;
        Log("切り取り画面が外から閉じられた（" + e.CloseReason + "）。待機に戻す");
        _overlay = null;
        _grab?.Dispose(); _grab = null;
        _cutWindow = null;
        _machine.Reset();
    }

    // 一言を書いている間に、文字認識（R-18）とブラウザの URL（R-19）を別のスレッドで読み始める
    private void StartReaders()
    {
        if (_grab == null) return;
        _ocrTask = OcrReader.ReadAsync(SnipImage.Crop(_grab.Screen, _cut));   // 赤枠などを描く前の画像
        var w = SnipSource.Window(_cutWindow, _grab.Windows, _cut);
        _urlTask = w != null && UrlText.IsBrowser(w.AppName) ? UrlReader.ReadAsync(w.Handle) : null;
    }

    // 待ちすぎない（文字認識は 3 秒、URL は 1 秒）。失敗は記録して空にする
    private T? WaitFor<T>(Task<T>? task, int ms, T? timedOut, string what)
    {
        if (task == null) return default;
        try { return task.Wait(ms) ? task.Result : timedOut; }
        catch (Exception ex) { Log($"{what}に失敗: {ex.GetBaseException().Message}"); return default; }
    }

    // 赤枠を焼き込んだ PNG を保存して一覧に加える（R-08・R-11）。書けなければ編集画面のまま知らせる（E-02）
    private bool AddToQueue()
    {
        if (_grab == null || _overlay == null) return false;
        var boxes = _overlay.Boxes;
        var marks = _overlay.Marks;
        var dir = Path.Combine(_settings.SaveDir, _grab.At.ToString("yyyyMMdd"));
        var path = Path.Combine(dir, _grab.At.ToString("HHmmss-fff") + ".png");
        try
        {
            Directory.CreateDirectory(dir);
            using (var bmp = SnipImage.Bake(_grab.Screen, _cut, boxes, marks, _overlay.DeviceDpi / 96f))
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            var (app, title) = SnipSource.Choose(_cutWindow, _grab.Windows, _cut, _grab.AppName, _grab.WindowTitle);   // R-10（r4）
            var ocr = WaitFor(_ocrTask, OcrWaitMs, OcrText.TimedOut, "文字認識");
            var url = WaitFor(_urlTask, UrlWaitMs, null, "URL の読み取り");
            _queue.Add(new SnipItem(path, _overlay.Comment, boxes, app, title, _grab.At, string.IsNullOrEmpty(ocr) ? null : ocr, url));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException)
        {
            try { File.Delete(path); } catch { }
            _machine.AddFailed();
            _notify($"保存できませんでした（{ex.Message}）。編集中の内容はそのままです", ToolTipIcon.Error);
            Log("E-02: " + ex);
            return false;
        }
    }

    private void Send()
    {
        var r = _sender.Send(_queue, DateTime.Now);
        switch (r.Outcome)
        {
            case SendOutcome.NothingToSend: _notify(Sender.WithSkipped("送るものがありません", r.Skipped), ToolTipIcon.Info); break;   // E-06・R-23
            case SendOutcome.Sent: _notify(Sender.WithSkipped("送る文と画像をコピーしました。Code タブに Ctrl＋V で貼ってください（画像は添付、送る文は .md で入ります）", r.Skipped), ToolTipIcon.None); break;
            case SendOutcome.NotWritten:   // verify S6
                _notify($"送る文を保存できませんでした（{r.Error}）。ためた分はそのままです", ToolTipIcon.Error); break;
            case SendOutcome.SentButNotCleared:   // verify-b V12
                _notify("送る文はコピーしました。ただ、ためる一覧を空にできませんでした（次に送ると同じ切り取りも入ります。一覧から消してください）", ToolTipIcon.Warning); break;
            case SendOutcome.SavedToFile: _notify($"クリップボードに書けませんでした。送る文を保存しました: {r.FallbackPath}", ToolTipIcon.Warning); break;   // E-03
        }
    }

    private void CloseOverlay()
    {
        if (_overlay != null)
        {
            var f = _overlay; _overlay = null;
            f.FormClosed -= OnOverlayClosed;
            f.Close(); f.Dispose();
        }
        _grab?.Dispose(); _grab = null;
        _cutWindow = null;
        _ocrTask = null; _urlTask = null;   // 読み終われば画像は読み取り側で解放する
    }

    private void TrySaveSettings()
    {
        if (!_settings.CanSave) return;   // 読めなかった・値を正した設定は書き戻さない（verify-b V14）
        try { _settings.Save(_settingsPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log("設定を保存できません: " + ex.Message); }
    }

    public void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(_settings.SaveDir);
            File.AppendAllText(Path.Combine(_settings.SaveDir, "app.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch { }
    }

    public void Dispose()
    {
        _delay.Dispose();
        CloseOverlay();
    }

    private sealed class WinClipboard : IClipboardWriter
    {
        public bool TrySetFiles(IReadOnlyList<string> paths)
        {
            try
            {
                var list = new System.Collections.Specialized.StringCollection();
                foreach (var p in paths) list.Add(p);
                var data = new DataObject();
                data.SetFileDropList(list);
                Clipboard.SetDataObject(data, true);
                return true;
            }
            catch (ExternalException) { return false; }
        }
    }
}

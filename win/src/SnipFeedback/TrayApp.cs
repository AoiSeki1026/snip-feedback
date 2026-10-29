using System.Diagnostics;
using SnipFeedback.Core;

namespace SnipFeedback;

// 常駐の本体（design.md「構成」）: トレイのアイコンとメニュー、起動キー、日付の変化で自動で消す処理
internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly HotkeyWindow? _hotkey;
    private readonly Settings _settings;
    private readonly SnipQueue _queue;
    private readonly SnipController _controller;
    private readonly System.Windows.Forms.Timer _dayTimer = new() { Interval = 60_000 };
    private readonly DayWatcher _day = new(DateTime.Now);
    private QueueForm? _queueForm;
    private HistoryForm? _historyForm;
    private IntPtr _iconHandle;
    private readonly BadgeForm _badge = new();   // R-20: ためている間の浮かせた表示
    private readonly string _settingsPath;

    public TrayApp()
    {
        var settingsPath = Settings.DefaultPath;
        _settingsPath = settingsPath;
        _settings = Settings.Load(settingsPath, out var settingsError);
        _queue = new SnipQueue(Path.Combine(_settings.SaveDir, "queue.json"));   // R-16: 前回のためる一覧を復元

        var menu = new ContextMenuStrip();
        menu.Items.Add("送る", null, (_, _) => _controller!.TraySend());
        menu.Items.Add("一覧", null, (_, _) => ShowQueue());
        menu.Items.Add("履歴", null, (_, _) => ShowHistory());   // R-21: 送った単位の履歴から入れ直す
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("保存先を開く", null, (_, _) => { Directory.CreateDirectory(_settings.SaveDir); Process.Start("explorer.exe", _settings.SaveDir); });
        menu.Items.Add("設定ファイルを開く", null, (_, _) => Process.Start(new ProcessStartInfo("notepad.exe", settingsPath)));
        menu.Items.Add(new ToolStripSeparator());
        // 見張り（タスク スケジューラ）が 5 分ごとに起動し直すので、「終了」は次のサインインまで止める印を置いてから終える（H-M35 1a）
        menu.Items.Add("終了（次のサインインまで止める）", null, (_, _) =>
        {
            try { new PauseFlag().Set(); } catch (Exception ex) { _controller!.Log("止める印を置けません: " + ex.Message); }
            _controller!.Log("トレイの「終了」で止めた（次のサインインまで）");
            ExitThread();
        });
        _icon = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => ShowQueue();

        _controller = new SnipController(_settings, settingsPath, _queue, (msg, kind) => _icon.ShowBalloonTip(4000, Program.AppName, msg, kind));
        _controller.HistoryRequested += ShowHistory;
        _queue.Changed += UpdateIcon;
        _queue.Changed += UpdateBadge;
        _badge.SendClicked += _controller.TraySend;
        _badge.ListClicked += ShowQueue;
        _badge.Moved += p =>
        {
            _settings.BadgeX = p.X; _settings.BadgeY = p.Y;
            if (!_settings.CanSave) return;   // 読めなかった・正した設定は書き戻さない（verify-b V14）
            try { _settings.Save(_settingsPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _controller.Log("位置を保存できません: " + ex.Message); }
        };
        // 受け止められなかった例外は記録して動き続ける（verify V2。Program で CatchException にしている）
        Application.ThreadException += (_, e) =>
        {
            _controller.Log("受け止められなかった例外: " + e.Exception);
            Notify("予期しないエラーが起きました（記録: app.log）", ToolTipIcon.Error);
        };
        UpdateIcon();
        UpdateBadge();

        if (settingsError != null) Notify(settingsError, ToolTipIcon.Warning);
        if (_queue.LoadError != null) Notify($"ためる一覧を読めませんでした（{_queue.LoadError}）。空の一覧で始めます", ToolTipIcon.Warning);   // verify-b V15

        // 起動キー（N-04）。読めなければ既定の Ctrl＋Alt＋S。登録できなければ知らせる（E-01）
        var spec = _settings.Hotkey;
        if (!HotkeySpec.TryParse(spec, out var mods, out var vk))
        {
            Notify($"起動キー「{spec}」を読めません。Ctrl+Alt+S で動きます。設定ファイル: {settingsPath}", ToolTipIcon.Warning);
            spec = "Ctrl+Alt+S"; HotkeySpec.TryParse(spec, out mods, out vk);
        }
        _hotkey = new HotkeyWindow(mods, (Keys)vk);
        if (_hotkey.Registered) _hotkey.Pressed += _controller.Hotkey;
        else Notify($"起動キー {spec} を登録できません（他のアプリが使っています）。設定ファイルで変えてください: {settingsPath}", ToolTipIcon.Error);
        _controller.Log($"起動: 起動キー {spec} {(_hotkey.Registered ? "登録" : "登録失敗 " + _hotkey.Error)}、ためる一覧 {_queue.Items.Count} 件");

        // R-15: 起動時と日付が変わったときに古い画像を消す
        RunCleanup();
        _dayTimer.Tick += (_, _) => { if (_day.Changed(DateTime.Now)) { _controller.Log("日付が変わったので古い画像を確かめます（R-15）"); RunCleanup(); } };
        _dayTimer.Start();

        // 起動の邪魔をしないよう少し待ってから準備する（R-01 の初回の遅さ）
        var warm = new System.Windows.Forms.Timer { Interval = 1500 };
        warm.Tick += (_, _) => { warm.Stop(); warm.Dispose(); try { _controller.WarmUp(); } catch (Exception ex) { _controller.Log("準備に失敗: " + ex.Message); } };
        warm.Start();
    }

    private void RunCleanup()
    {
        if (!_queue.CanCleanup) { _controller.Log("ためる一覧を読めなかったので、古い画像の自動削除を止めています（verify-c V21）"); return; }
        // 今の一覧の画像と、控え（読めなかった一覧の複製）に載っている画像は消さない（T-09b、verify-d V28）。
        // 読めない控えがあれば、その控えにだけ載る画像を消さないよう、この回は止める（verify T2）
        try
        {
            var backup = Cleanup.BackupReferences(_settings.SaveDir, DateTime.Today, _settings.KeepDays, out var complete);   // try の中（verify-b U2）
            if (!complete) { _controller.Log("読めない控え（queue.json.unread-*・broken-*）があるので、古い画像の自動削除を止めています（控えが期限を過ぎれば再開）"); return; }
            var n = Cleanup.Run(_settings.SaveDir, DateTime.Today, _settings.KeepDays, _queue.Items.Select(i => i.ImagePath).Concat(backup));
            if (n > 0) _controller.Log($"{_settings.KeepDays} 日を過ぎた {n} 件を消しました");
        }
        catch (Exception ex) { _controller.Log("古い画像の自動削除に失敗: " + ex.Message); }   // 起動のたびに落ちないように（verify T1）
    }

    // R-20: 1 件以上なら右下（または覚えた位置）に出し、0 件なら隠す
    private void UpdateBadge()
    {
        var n = _queue.Items.Count;
        if (n == 0) { _badge.Hide(); return; }
        _badge.SetCount(n);
        if (!_badge.Visible)
        {
            var work = Screen.PrimaryScreen!.WorkingArea;
            Point? saved = _settings.BadgeX is int x && _settings.BadgeY is int y ? new Point(x, y) : null;
            _badge.PlaceExpanded(BadgePlacement.Locate(work, _badge.ExpandedSize, saved, (int)(16 * _badge.DeviceDpi / 96f)));
            _badge.Show();
        }
    }

    private void Notify(string msg, ToolTipIcon kind) => _icon.ShowBalloonTip(5000, Program.AppName, msg, kind);

    private void ShowQueue()
    {
        if (_queueForm is { IsDisposed: false }) { _queueForm.Activate(); return; }
        _queueForm = new QueueForm(_queue, _controller.TraySend);
        _queueForm.Show();
    }

    // R-21: 開くたびに保存先を読み直す（開いていれば閉じて開き直す。前の内容のままにしない）
    private void ShowHistory()
    {
        if (_historyForm is { IsDisposed: false }) _historyForm.Close();
        _historyForm = new HistoryForm(_settings.SaveDir, _controller.Resend);
        _historyForm.Show();
    }

    // R-12: ためている件数をアイコンに描く（H-M33: アプリのアイコンの右下に赤い丸と数字）
    private void UpdateIcon()
    {
        var n = _queue.Items.Count;
        using var bmp = DrawTrayIcon(Math.Max(SystemInformation.SmallIconSize.Width, 16), n);
        var h = bmp.GetHicon();
        var old = _icon.Icon; var oldHandle = _iconHandle;
        _icon.Icon = Icon.FromHandle(h); _iconHandle = h;
        old?.Dispose();
        if (oldHandle != IntPtr.Zero) Native.DestroyIcon(oldHandle);
        _icon.Text = n > 0 ? $"{Program.AppName}（{n} 件）" : Program.AppName;
    }

    internal static Bitmap DrawTrayIcon(int px, int n)
    {
        var bmp = new Bitmap(px, px);
        using (var g = Graphics.FromImage(bmp))
        {
            // アプリの絵は滑らかにする設定の前に描く（後だと半画素ずれて縁が薄くなる）
            using (var app = Program.AppIcon(px)) g.DrawIcon(app, new Rectangle(0, 0, px, px));
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            if (n > 0)
            {
                var d = px * 0.68f;   // 丸の直径。小さいトレイでも数字が読める大きさ
                var rc = new RectangleF(px - d, px - d, d, d);
                using var bg = new SolidBrush(SnipImage.Red);
                using var edge = new Pen(Color.White, Math.Max(1f, px / 16f));
                g.FillEllipse(bg, rc);
                g.DrawEllipse(edge, rc);
                var label = n > 99 ? "99" : n.ToString();
                using var font = new Font("Segoe UI", d * (label.Length > 1 ? 0.55f : 0.72f), FontStyle.Bold, GraphicsUnit.Pixel);
                using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(label, font, Brushes.White, rc, center);
            }
        }
        return bmp;
    }

    protected override void ExitThreadCore()
    {
        _dayTimer.Dispose();
        _hotkey?.Dispose();
        _controller.Dispose();
        _queueForm?.Close();
        _historyForm?.Close();
        _badge.Close(); _badge.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        if (_iconHandle != IntPtr.Zero) Native.DestroyIcon(_iconHandle);
        base.ExitThreadCore();
    }
}

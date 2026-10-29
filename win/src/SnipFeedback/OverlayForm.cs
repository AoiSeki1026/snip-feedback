using SnipFeedback.Core;

namespace SnipFeedback;

// 切り取り画面と編集画面（同じ全面のフォーム。design.md）。止めた画面を表示し、入力を SnipController へ渡す。
// 判断（状態遷移）は SnipStateMachine が持ち、このフォームは描画と入力の受け渡しだけをする
internal sealed class OverlayForm : Form
{
    public event Action<SnipEvent>? KeyEvent;                    // D・Enter・Ctrl＋Enter・Esc
    public event Action<Rectangle, WindowInfo?>? CutRequested;   // 切り取る範囲（画面の画素座標）
    public event Action<Rectangle>? BoxRequested;                // 赤枠（画面の画素座標）
    public event Action<SnipMode>? ModeChanged;

    private readonly Grab _grab;
    private readonly Rectangle? _lastRect;
    private SnipMode _mode;
    private Point? _down;
    private Rectangle _drag;
    private WindowInfo? _hover;

    private bool _editing;
    private Rectangle _cut;
    private TextBox? _comment;
    // 描いたもの（枠・矢印・線）は Sketch が描いた順に持つ（R-17）。枠ごとの一言の欄はここで持つ
    private readonly Sketch _sketch = new();
    private readonly Dictionary<Sketch.Item, TextBox> _boxText = new();
    private SketchKind _dragKind;
    private Point _dragEnd;
    private List<Point>? _penPoints;
    // 描いている間は一言の欄を隠す（枠ごとの欄が次の枠・矢印にかぶって見えない。H-M36）
    private bool _textHidden;
    private Control? _focusBeforeDrag;

    private float DpiScale => DeviceDpi / 96f;

    public OverlayForm(Grab grab, SnipMode mode, Rectangle? lastRect)
    {
        _grab = grab; _mode = mode; _lastRect = lastRect;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = grab.Bounds;
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        Text = Program.AppName;
    }

    public SnipMode Mode => _mode;
    public Rectangle CutArea => _cut;

    // 編集画面の内容（一言と赤枠。赤枠は切り取った画像の中の座標）
    public string Comment => _comment?.Text ?? "";
    public List<SnipBox> Boxes => _sketch.Items.Where(i => i.Kind == SketchKind.Box)
        .Select((b, k) => new SnipBox(k + 1, b.Box.X - _cut.X, b.Box.Y - _cut.Y, b.Box.Width, b.Box.Height,
            _boxText.TryGetValue(b, out var tb) ? tb.Text : ""))
        .ToList();
    // 矢印と線（切り取った画像の中の座標。R-17）
    public List<SnipMark> Marks => _sketch.Marks
        .Select(m => m with { Points = m.Points.Select(p => new Point(p.X - _cut.X, p.Y - _cut.Y)).ToList() })
        .ToList();

    // ---------- 編集画面へ（R-07） ----------
    public void EnterEditing(Rectangle cut)
    {
        _editing = true; _cut = cut; _down = null; _hover = null;
        Cursor = Cursors.Cross;
        _comment = NewTextBox("一言（Enter で送る／Ctrl＋Enter でためるだけ／Esc で捨てる／Shift＋Enter で改行）",
            CommentBounds(cut), multiline: true);
        Controls.Add(_comment);
        _comment.GotFocus += (_, _) => _comment.BringToFront();   // 入力中の欄を最前面に（R-22）
        _comment.Focus();
        Invalidate();
    }

    // 赤枠を足し、その枠への一言の欄を開く（R-08）。欄は 2 行で開き、入力中は打った分だけ伸び、
    // 焦点が外れたら 2 行に畳む。▼▲ で全文と 2 行を切り替える。欄どうしがなるべく重ならない位置に置く（R-22、r13）
    public void AddBox(Rectangle r)
    {
        var item = _sketch.AddBox(r);
        var n = _sketch.NumberOf(item);
        var tb = NewTextBox(BoxPlaceholder(n), new Rectangle(r.Right, r.Top, MemoWidth, 1), multiline: true);
        tb.ScrollBars = ScrollBars.None;   // 伸び縮みするので縦のスクロールバーは出さない（超えた分は打っている所へスクロール）
        Controls.Add(tb);
        _boxText[item] = tb;
        var more = new Label
        {
            AutoSize = false, Size = new Size(MemoToggleWidth, (int)(18 * DpiScale)), Text = "▼", TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand, BackColor = Color.FromArgb(230, 230, 230), Font = _textFont, Visible = false,
        };
        tb.Controls.Add(more);   // 欄の右端。押しても焦点は動かない（Label は焦点を取らない）
        _memoToggle[tb] = more;
        more.Click += (_, _) => { if (!_memoExpanded.Remove(tb)) _memoExpanded.Add(tb); LayoutMemo(item, focused: false); };
        Native.SendMessage(tb.Handle, Native.EM_SETMARGINS, (IntPtr)Native.EC_RIGHTMARGIN, (IntPtr)(MemoToggleWidth << 16));   // 文字が ▼▲ の下に入らない
        // 欄を隠している間（描いている間。H-M36）は、焦点が残りの欄へ順に移っても大きさ・位置・▼ の状態を変えない（verify-h-m40 F1）
        tb.GotFocus += (_, _) => { if (_textHidden) return; tb.BringToFront(); LayoutMemo(item, focused: true); };   // 入力中の欄を最前面に
        tb.LostFocus += (_, _) => { if (_textHidden) return; _memoExpanded.Remove(tb); LayoutMemo(item, focused: false); };
        tb.TextChanged += (_, _) => LayoutMemo(item, tb.Focused);
        tb.Focus();   // 前の欄が畳まれてから（LostFocus）、この欄を置く（GotFocus）
        if (!_memoPlaced.Contains(tb)) LayoutMemo(item, focused: tb.Focused);   // 焦点を移せなかったとき（隠している間など）も置く
        Invalidate();
    }

    private static string BoxPlaceholder(int n) => $"枠{n} の一言（空でもよい）";

    // ---------- 枠への一言の欄の大きさと置き場所（R-22、r13、H-M40） ----------
    private readonly Dictionary<TextBox, Label> _memoToggle = new();
    private readonly HashSet<TextBox> _memoExpanded = new();   // ▼ で全文にしている欄（焦点が外れたら戻す）
    private readonly HashSet<TextBox> _memoPlaced = new();     // 一度置いた欄（以後は今の位置で重ならなければ動かさない）
    private int MemoWidth => (int)(320 * DpiScale);
    private int MemoToggleWidth => (int)(20 * DpiScale);

    private int LineHeight
    {
        get
        {
            var one = TextRenderer.MeasureText("あ", _textFont).Height;
            return TextRenderer.MeasureText("あ\nあ", _textFont).Height - one;
        }
    }

    private void LayoutMemo(Sketch.Item item, bool focused)
    {
        if (!_boxText.TryGetValue(item, out var tb) || tb.IsDisposed) return;
        var content = tb.GetLineFromCharIndex(tb.TextLength) + 1;   // 折り返しを含む表示の行数
        var lines = MemoLayout.Lines(focused, _memoExpanded.Contains(tb), content);
        var frame = tb.Height - tb.ClientSize.Height;
        var size = new Size(MemoWidth, lines * LineHeight + frame + (int)(4 * DpiScale));
        var obstacles = _boxText.Where(kv => kv.Value != tb && !kv.Value.IsDisposed).Select(kv => kv.Value.Bounds)
            .Concat(_comment is { IsDisposed: false } c ? new[] { c.Bounds } : Array.Empty<Rectangle>())
            .Concat(_sketch.Items.Where(i => i.Kind == SketchKind.Box).Select(i => i.Box))   // 自分の枠も含む
            .ToList();
        Point? keep = _memoPlaced.Contains(tb) ? tb.Location : null;
        tb.Bounds = MemoLayout.Place(item.Box, size, ClientRectangle, obstacles, (int)(4 * DpiScale), keep);
        _memoPlaced.Add(tb);
        // 全部が収まる高さなら先頭の行から見せる（畳んだときは先頭の 2 行）。入力中は打っている所を見せる
        if (!focused || content <= lines) Native.SendMessage(tb.Handle, Native.EM_LINESCROLL, IntPtr.Zero, (IntPtr)(-100000));
        if (focused) tb.ScrollToCaret();
        if (_memoToggle.TryGetValue(tb, out var more))
        {
            more.Visible = !focused && MemoLayout.HasMore(content);
            more.Text = _memoExpanded.Contains(tb) ? "▲" : "▼";
            more.Location = new Point(tb.ClientSize.Width - more.Width, 0);
        }
    }

    // 右クリックで最後に描いたものを1つ消す。枠なら一言の欄も消し、残りの枠の番号を振り直す（R-17）
    private void RemoveLast()
    {
        var it = _sketch.RemoveLast();
        if (it == null) return;
        if (it.Kind == SketchKind.Box && _boxText.Remove(it, out var tb))
        {
            // 焦点のある欄を外すと、焦点が残りの枠の欄へ移って欄を動かしうるので、先に全体の一言の欄へ移す（verify-h-m40 F1）
            if (tb.Focused) _comment?.Focus();
            _memoToggle.Remove(tb); _memoExpanded.Remove(tb); _memoPlaced.Remove(tb);
            Controls.Remove(tb); tb.Dispose();
            foreach (var (box, text) in _boxText) text.PlaceholderText = BoxPlaceholder(_sketch.NumberOf(box));
        }
        Invalidate();
    }

    private Rectangle CommentBounds(Rectangle cut)
    {
        var s = DpiScale;
        var w = Math.Min(Math.Max(cut.Width, (int)(520 * s)), Width - (int)(16 * s));
        var h = (int)(64 * s);
        var x = Math.Clamp(cut.X, (int)(8 * s), Width - w - (int)(8 * s));
        var gap = (int)(8 * s);
        int y = cut.Bottom + gap + h <= Height ? cut.Bottom + gap          // 下
              : cut.Top - gap - h >= 0 ? cut.Top - gap - h                  // 上
              : cut.Bottom - h - gap;                                       // 中の下
        return new Rectangle(x, y, w, h);
    }

    private TextBox NewTextBox(string placeholder, Rectangle bounds, bool multiline)
    {
        var tb = new TextBox
        {
            Multiline = multiline,
            AcceptsReturn = false,
            PlaceholderText = placeholder,
            Font = _textFont,
            Bounds = bounds,
            BorderStyle = BorderStyle.FixedSingle,
        };
        if (multiline) tb.ScrollBars = ScrollBars.Vertical;
        return tb;
    }

    // ---------- キー（R-02・R-05・R-06・R-09） ----------
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_editing)
        {
            if (e.KeyCode == Keys.Enter && e.Shift && ActiveControl is TextBox { Multiline: true } tb)
            {
                // Shift＋Enter は改行
                var at = tb.SelectionStart;
                tb.Text = tb.Text.Remove(at, tb.SelectionLength).Insert(at, Environment.NewLine);
                tb.SelectionStart = at + Environment.NewLine.Length;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Enter && e.Control) { e.SuppressKeyPress = true; KeyEvent?.Invoke(SnipEvent.CtrlEnter); }
            else if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; KeyEvent?.Invoke(SnipEvent.Enter); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; KeyEvent?.Invoke(SnipEvent.Esc); }
            return;   // D・R・W・Space は文字として入力する
        }
        switch (e.KeyCode)
        {
            case Keys.R: SetMode(SnipMode.Rect); break;
            case Keys.W: SetMode(SnipMode.Window); break;
            case Keys.Space: if (_lastRect is { } r) CutRequested?.Invoke(r, null); break;
            case Keys.D: KeyEvent?.Invoke(SnipEvent.Delay); break;
            case Keys.H: KeyEvent?.Invoke(SnipEvent.History); break;   // r9: 送った単位の履歴（R-21）
            case Keys.Escape: KeyEvent?.Invoke(SnipEvent.Esc); break;
            case Keys.Enter: KeyEvent?.Invoke(e.Control ? SnipEvent.CtrlEnter : SnipEvent.Enter); break;
        }
        e.SuppressKeyPress = true;
    }

    private void SetMode(SnipMode m)
    {
        if (_mode == m) return;
        _mode = m; _hover = null;
        ModeChanged?.Invoke(m);
        Invalidate();
    }

    // ---------- マウス（R-03・R-04・R-08） ----------
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_editing && e.Button == MouseButtons.Right && _cut.Contains(e.Location)) { RemoveLast(); return; }   // R-17
        if (e.Button != MouseButtons.Left) return;
        if (_editing && !_cut.Contains(e.Location)) return;   // 編集画面では切り取った画像の上だけ
        _down = e.Location; _drag = Rectangle.Empty; _dragEnd = e.Location;
        // 編集画面: Shift＋ドラッグで矢印、Ctrl＋ドラッグで線、そのままなら枠（R-17）
        _dragKind = !_editing ? SketchKind.Box
                  : (ModifierKeys & Keys.Shift) != 0 ? SketchKind.Arrow
                  : (ModifierKeys & Keys.Control) != 0 ? SketchKind.Pen
                  : SketchKind.Box;
        _penPoints = _dragKind == SketchKind.Pen ? new List<Point> { e.Location } : null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_down is { } d)
        {
            if (_editing && !_textHidden && Math.Abs(e.X - d.X) + Math.Abs(e.Y - d.Y) >= 4) HideTexts();   // H-M36
            var p = _editing ? Clamp(e.Location, _cut) : e.Location;
            _dragEnd = p;
            if (_dragKind == SketchKind.Pen) _penPoints?.Add(p);
            else if (_dragKind == SketchKind.Box)
            {
                var r = Rect(d, e.Location);
                if (_editing) r = Rectangle.Intersect(r, _cut);
                _drag = r;
            }
            Invalidate();
        }
        else if (!_editing && _mode == SnipMode.Window)
        {
            var w = WindowPicker.Pick(_grab.Windows, e.Location);
            if (w != _hover) { _hover = w; Invalidate(); }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || _down is not { } d) return;
        _down = null;
        var r = _drag; _drag = Rectangle.Empty;
        var moved = Math.Abs(e.X - d.X) >= SnipStateMachine.MinSize || Math.Abs(e.Y - d.Y) >= SnipStateMachine.MinSize;
        if (_editing)
        {
            ShowTexts();   // H-M36: 離したら戻す（新しい枠なら AddBox がその欄に焦点を移す）
            if (!moved) { _penPoints = null; Invalidate(); return; }
            if (_dragKind == SketchKind.Arrow) _sketch.AddArrow(d, _dragEnd);
            else if (_dragKind == SketchKind.Pen && _penPoints is { Count: >= 2 } pts) _sketch.AddPen(pts);
            else if (_dragKind == SketchKind.Box) BoxRequested?.Invoke(r);
            _penPoints = null;
        }
        else if (_mode == SnipMode.Window && !moved)
        {
            var w = WindowPicker.Pick(_grab.Windows, e.Location);
            if (w != null) CutRequested?.Invoke(w.Bounds, w);
        }
        else CutRequested?.Invoke(r, null);   // 8 画素未満は SnipStateMachine が無視する（E-04）
        Invalidate();
    }

    // H-M36: 一言の欄（切り取り全体の欄と枠ごとの欄）を隠す・戻す。戻すときは隠す前に焦点があった欄へ
    private IEnumerable<Control> TextBoxes() => _boxText.Values.Cast<Control>().Prepend(_comment!).Where(c => c != null);

    private void HideTexts()
    {
        _focusBeforeDrag = ActiveControl;
        _textHidden = true;   // 先に立てる。隠すたびに焦点が次の欄へ移り、欄の GotFocus／LostFocus が走るため（verify-h-m40 F1）
        foreach (var c in TextBoxes()) c.Visible = false;
    }

    private void ShowTexts()
    {
        if (!_textHidden) return;
        foreach (var c in TextBoxes()) if (!c.IsDisposed) c.Visible = true;
        // 隠す前の欄が無くなっていたら（右クリックで消した枠の欄など）切り取り全体の欄へ（verify-b K13）
        if (_focusBeforeDrag is { IsDisposed: false, Visible: true } f) f.Focus();
        else _comment?.Focus();
        _focusBeforeDrag = null;
        // 焦点を戻し終えてから下ろす。隠している間に焦点が移った先の欄から焦点が外れても、畳まない（verify-h-m40 F1）
        _textHidden = false;
    }

    // ドラッグ中にマウスのキャプチャが外れたら（別の窓が前に出たなど）、描きかけをやめて欄を戻す（verify-b K14）
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (_down == null || Capture) return;
        _down = null; _drag = Rectangle.Empty; _penPoints = null;
        ShowTexts();
        Invalidate();
    }

    private static Point Clamp(Point p, Rectangle r) =>
        new(Math.Clamp(p.X, r.Left, r.Right - 1), Math.Clamp(p.Y, r.Top, r.Bottom - 1));

    private static Rectangle Rect(Point a, Point b) =>
        Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    // ---------- 描画 ----------
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        // 画像の DPI に頼らず、画面の画素と 1:1 で描く（verify V10）
        g.DrawImage(_grab.Screen, new Rectangle(0, 0, _grab.Screen.Width, _grab.Screen.Height), 0, 0, _grab.Screen.Width, _grab.Screen.Height, GraphicsUnit.Pixel);
        using var dim = new SolidBrush(Color.FromArgb(_editing ? 140 : 70, 0, 0, 0));
        Rectangle? clear = _editing ? _cut : _drag.Width > 0 ? _drag : _hover?.Bounds;
        using (var region = new Region(ClientRectangle))
        {
            if (clear is { } c) region.Exclude(c);
            g.FillRegion(dim, region);
        }
        var s = DpiScale;
        using var pen = new Pen(SnipImage.Red, Math.Max(2f, 2f * s));
        if (!_editing && clear is { } sel) g.DrawRectangle(pen, sel);
        if (_editing)
        {
            SnipImage.DrawMarks(g, _sketch.Marks, s);
            SnipImage.DrawBoxes(g, _sketch.Boxes, s);
            if (_drag.Width > 0) g.DrawRectangle(pen, _drag);
            // 描いている途中の矢印・線
            if (_down is { } d0 && _dragKind == SketchKind.Arrow)
                SnipImage.DrawMarks(g, new[] { new SnipMark(SketchKind.Arrow, new List<Point> { d0, _dragEnd }) }, s);
            if (_penPoints is { Count: >= 2 } pp)
                SnipImage.DrawMarks(g, new[] { new SnipMark(SketchKind.Pen, pp) }, s);
        }
        DrawHint(g, s);
    }

    private void DrawHint(Graphics g, float s)
    {
        // 送り忘れないよう、ためている件数と送り方を常に出す（r2・R-14）
        var text = _editing
            ? $"Enter: 送る（{QueuedCount + 1} 件）　Ctrl＋Enter: ためるだけ（今 {QueuedCount} 件）　Esc: 捨てる\nドラッグ: 赤枠　Shift＋ドラッグ: 矢印　Ctrl＋ドラッグ: 線　右クリック: 1つ戻す"
            : $"ためている {QueuedCount} 件　Enter: 送る　H: 履歴\n"
              + $"{(_mode == SnipMode.Rect ? "［四角］" : "［ウィンドウ］")}　R: 四角　W: ウィンドウ　Space: 前回の範囲　D: {DelayLabel}　Esc: やめる";
        var font = _textFont;
        var size = g.MeasureString(text, font);
        var pad = 8 * s;
        var rect = new RectangleF((Width - size.Width) / 2 - pad, 12 * s, size.Width + pad * 2, size.Height + pad);
        using var bg = new SolidBrush(Color.FromArgb(220, 30, 30, 30));
        g.FillRectangle(bg, rect);
        g.DrawString(text, font, Brushes.White, rect.X + pad, rect.Y + pad / 2);
    }

    public string DelayLabel { get; set; } = "3秒後";

    // 入力欄と案内で1つのフォントを使い、閉じるときに解放する（verify V10）
    private readonly Font _textFont = new("Yu Gothic UI", 11f);
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);   // 入力欄を先に解放してから、共有のフォントを解放する（verify-b V20）
        if (disposing) _textFont.Dispose();
    }
    public int QueuedCount { get; set; }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
    }
}

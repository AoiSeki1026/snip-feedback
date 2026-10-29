using System.Runtime.InteropServices;
using SnipFeedback.Core;

namespace SnipFeedback;

// R-20（H-M32）: ためている間、画面の右下に浮かせる小さな表示。
// 焦点を奪わない（WS_EX_NOACTIVATE）・タスクバーに出ない・切り取った画像に写らない（WDA_EXCLUDEFROMCAPTURE）。
// 件数を押すと一覧、「送る」で送信、「－」で件数だけの丸に畳む（丸を押すと戻す）。ドラッグで動かせる
internal sealed class BadgeForm : Form
{
    public event Action? SendClicked;
    public event Action? ListClicked;
    public event Action<Point>? Moved;   // 広げたときの左上（覚える位置）

    private const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    private readonly Label _count = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.Hand };
    private readonly Button _send = new() { Text = "送る", FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
    private readonly Button _min = new() { Text = "－", FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
    private readonly Font _font = new("Yu Gothic UI", 10f, FontStyle.Bold);
    private bool _minimized;
    private int _n;
    private Point _down; private bool _dragging, _pressed;

    private float S => DeviceDpi / 96f;
    public Size ExpandedSize => new((int)(230 * S), (int)(40 * S));
    private Size SmallSize => new((int)(40 * S), (int)(40 * S));

    public BadgeForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = SnipImage.Red;
        ForeColor = Color.White;
        Font = _font;
        Text = Program.AppName;
        foreach (var b in new[] { _send, _min })
        {
            b.FlatAppearance.BorderColor = Color.White;
            b.BackColor = Color.FromArgb(200, 40, 40);
            b.ForeColor = Color.White;
            b.Click += (_, _) => { if (b == _send) SendClicked?.Invoke(); else SetMinimized(true); };
        }
        Controls.AddRange(new Control[] { _count, _send, _min });
        foreach (Control c in new Control[] { this, _count })
        {
            c.MouseDown += OnDown; c.MouseMove += OnMove; c.MouseUp += OnUp;
        }
        Size = ExpandedSize;
        Layout_();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST; return cp; }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE);   // 切り取りに写さない（R-20）
    }

    public void SetCount(int n) { _n = n; Layout_(); }

    // 広げたときの左上を指定して置く（畳んでいるときは右下をそろえる）
    public void PlaceExpanded(Point topLeft)
    {
        var big = ExpandedSize;
        var right = topLeft.X + big.Width; var bottom = topLeft.Y + big.Height;
        var sz = _minimized ? SmallSize : big;
        Bounds = new Rectangle(right - sz.Width, bottom - sz.Height, sz.Width, sz.Height);
    }

    private Point ExpandedTopLeft => new(Right - ExpandedSize.Width, Bottom - ExpandedSize.Height);

    private void SetMinimized(bool m)
    {
        var anchor = ExpandedTopLeft;
        _minimized = m;
        if (!m)
        {
            // 広げたときに作業領域からはみ出さないよう収める（verify S9）
            var work = Screen.FromControl(this).WorkingArea;
            var big = ExpandedSize;
            anchor = new Point(Math.Clamp(anchor.X, work.Left, work.Right - big.Width), Math.Clamp(anchor.Y, work.Top, work.Bottom - big.Height));
        }
        PlaceExpanded(anchor);
        Layout_();
    }

    private void SetRegion(Region? r)
    {
        var old = Region;
        Region = r;
        old?.Dispose();   // verify S9
    }

    private void Layout_()
    {
        var s = S;
        _send.Visible = _min.Visible = !_minimized;
        if (_minimized)
        {
            _count.Bounds = new Rectangle(0, 0, Width, Height);
            _count.TextAlign = ContentAlignment.MiddleCenter;
            _count.Text = _n.ToString();
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddEllipse(0, 0, Width, Height);
            SetRegion(new Region(path));
        }
        else
        {
            SetRegion(null);
            var bw = (int)(56 * s); var mw = (int)(32 * s); var pad = (int)(6 * s); var h = Height - pad * 2;
            _min.Bounds = new Rectangle(Width - pad - mw, pad, mw, h);
            _send.Bounds = new Rectangle(_min.Left - pad - bw, pad, bw, h);
            _count.Bounds = new Rectangle(pad * 2, 0, _send.Left - pad * 3, Height);
            _count.TextAlign = ContentAlignment.MiddleLeft;
            _count.Text = $"ためている {_n} 件";
        }
    }

    // ドラッグで動かす。動かさずに離したらクリック（広げているときは件数で一覧、畳んでいるときは広げる）
    private void OnDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _pressed = true; _dragging = false; _down = Cursor.Position;
    }

    private void OnMove(object? sender, MouseEventArgs e)
    {
        if (!_pressed) return;
        var p = Cursor.Position;
        if (!_dragging && Math.Abs(p.X - _down.X) + Math.Abs(p.Y - _down.Y) < (int)(4 * S)) return;
        _dragging = true;
        Location = new Point(Location.X + p.X - _down.X, Location.Y + p.Y - _down.Y);
        _down = p;
    }

    private void OnUp(object? sender, MouseEventArgs e)
    {
        if (!_pressed) return;
        _pressed = false;
        if (_dragging) { Moved?.Invoke(ExpandedTopLeft); return; }
        if (_minimized) SetMinimized(false);
        else ListClicked?.Invoke();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _font.Dispose();
    }
}

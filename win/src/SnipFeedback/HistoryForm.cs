using SnipFeedback.Core;

namespace SnipFeedback;

// 送った単位の履歴（R-21・E-08、r9）。1 行 1 送信。Enter かダブルクリックで、その送信を入れ直して閉じる。Esc で閉じる
internal sealed class HistoryForm : Form
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, IntegralHeight = false };
    private readonly Label _empty = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Text = "送った履歴はありません（送る文はふだん 7 日で消えます）", Visible = false };
    private readonly Label _hint = new() { Dock = DockStyle.Bottom, AutoSize = false, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0), Text = "Enter／ダブルクリック: クリップボードに入れる　Esc: 閉じる" };
    private readonly List<SendGroup> _groups;
    private readonly Dictionary<int, Image?> _thumbs = new();
    private readonly Action<SendGroup> _resend;
    private readonly int _thumbW, _thumbH;

    public HistoryForm(string saveDir, Action<SendGroup> resend)
    {
        _resend = resend;
        _groups = SendHistory.Load(saveDir);
        Text = $"{Program.AppName} — 送った履歴";
        Icon = Program.AppIcon(SystemInformation.IconSize.Width);
        Font = new Font("Yu Gothic UI", 10f);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        var s = DeviceDpi / 96f;
        _thumbW = (int)(160 * s); _thumbH = (int)(90 * s);
        Size = new Size((int)(720 * s), (int)(560 * s));
        _list.ItemHeight = Math.Min(255, _thumbH + (int)(12 * s));
        Controls.Add(_list);
        Controls.Add(_empty);
        Controls.Add(_hint);
        foreach (var g in _groups) _list.Items.Add(g);
        _empty.Visible = _groups.Count == 0;
        _list.Visible = _groups.Count > 0;
        _list.DrawItem += DrawRow;
        // 押した行だけを選ぶ。行の無い所・読めない行のダブルクリックでは何もしない（verify V2）
        _list.MouseDoubleClick += (_, e) => { var i = _list.IndexFromPoint(e.Location); if (i != ListBox.NoMatches && i == _list.SelectedIndex) Choose(i); };
        // ListBox は押した時点では黙って選択を変え、離したときに SelectedIndexChanged を出す（その時はボタンが離れている）。
        // マウスで動いたかは押した時点で覚えておく（verify V5）
        _list.MouseDown += (_, _) => _byMouse = true;
        _list.MouseUp += (_, _) => _byMouse = false;
        _list.KeyDown += (_, _) => _byMouse = false;
        _list.SelectedIndexChanged += (_, _) => SkipUnreadable();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Choose(_list.SelectedIndex); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); }
        };
        FormClosed += (_, _) => { foreach (var t in _thumbs.Values) t?.Dispose(); Icon?.Dispose(); };
        var firstReadable = _groups.FindIndex(g => g.Readable);
        if (firstReadable >= 0) _list.SelectedIndex = firstReadable;   // いちばん新しい送信（H → Enter で直前を入れ直せる）
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        _list.Focus();
    }

    private int _lastIndex = -1;
    private bool _byMouse;

    // 読めない行は選べない（E-08）。行き先は HistoryPick.Adjust（マウスなら元の行のまま）
    private void SkipUnreadable()
    {
        var i = _list.SelectedIndex;
        var to = HistoryPick.Adjust(_groups, i, _lastIndex, _byMouse);
        if (to != i) { _list.SelectedIndex = to; return; }
        _lastIndex = i;
    }

    private void Choose(int index)
    {
        if (HistoryPick.Choose(_groups, index) is not { } g) return;
        Close();
        _resend(g);
    }

    private void DrawRow(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _groups.Count) return;
        var g = _groups[e.Index];
        var selected = (e.State & DrawItemState.Selected) != 0 && g.Readable;
        e.Graphics.FillRectangle(selected ? SystemBrushes.Highlight : SystemBrushes.Window, e.Bounds);
        var fore = !g.Readable ? SystemColors.GrayText : selected ? SystemColors.HighlightText : SystemColors.WindowText;
        var pad = (int)(6 * DeviceDpi / 96f);
        var thumbRect = new Rectangle(e.Bounds.X + pad, e.Bounds.Y + pad, _thumbW, _thumbH);
        var thumb = Thumb(e.Index);
        if (thumb != null) e.Graphics.DrawImage(thumb, Fit(thumb.Size, thumbRect));
        else e.Graphics.DrawRectangle(SystemPens.ControlDark, thumbRect);
        var textX = thumbRect.Right + pad * 2;
        var textRect = new Rectangle(textX, e.Bounds.Y + pad, e.Bounds.Right - textX - pad, e.Bounds.Height - pad * 2);
        var head = HistoryPick.Head(g);
        var comment = g.Readable ? (string.IsNullOrWhiteSpace(g.FirstComment) ? "（一言なし）" : g.FirstComment) : "";
        using var bold = new Font(Font, FontStyle.Bold);
        TextRenderer.DrawText(e.Graphics, head, bold, textRect, fore, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
        var second = new Rectangle(textRect.X, textRect.Y + bold.Height + pad, textRect.Width, textRect.Height - bold.Height - pad);
        TextRenderer.DrawText(e.Graphics, comment, Font, second, fore, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
    }

    // いま在る最初の画像の縮小（1 枚目が消えていても 2 枚目から作る。verify M2）。ファイルは読んだらすぐ閉じる（自動削除・一覧からの削除を妨げない。QueueForm と同じ）
    private Image? Thumb(int index)
    {
        if (_thumbs.TryGetValue(index, out var t)) return t;
        Image? made = null;
        var g = _groups[index];
        if (g.Readable && g.Present.Count > 0)
        {
            try
            {
                using var fs = File.OpenRead(g.Present[0]);
                using var img = Image.FromStream(fs);
                var r = Fit(img.Size, new Rectangle(0, 0, _thumbW, _thumbH));
                made = new Bitmap(img, Math.Max(1, r.Width), Math.Max(1, r.Height));
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }
        }
        _thumbs[index] = made;
        return made;
    }

    private static Rectangle Fit(Size src, Rectangle box)
    {
        var k = Math.Min((float)box.Width / src.Width, (float)box.Height / src.Height);
        int w = (int)(src.Width * k), h = (int)(src.Height * k);
        return new Rectangle(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
    }
}

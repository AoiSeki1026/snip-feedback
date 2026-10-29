using SnipFeedback.Core;

namespace SnipFeedback;

// ためる一覧（R-12）。1件ずつ消せる。選んだ項目の画像を右に出す
internal sealed class QueueForm : Form
{
    private readonly SnipQueue _queue;
    private readonly ListBox _list = new() { Dock = DockStyle.Left, Width = 420, IntegralHeight = false };
    private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
    private readonly Button _remove = new() { Text = "選んだ1件を消す", AutoSize = true };
    private readonly Button _send = new() { Text = "送る", AutoSize = true };

    public QueueForm(SnipQueue queue, Action send)
    {
        _queue = queue;
        Text = $"{Program.AppName} — ためている切り取り";
        Icon = Program.AppIcon(SystemInformation.IconSize.Width);   // 大きいアイコン（Alt＋Tab）の大きさで選ぶ（verify V4）
        Size = new Size(1000, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Yu Gothic UI", 10f);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        bar.Controls.AddRange(new Control[] { _send, _remove });
        Controls.Add(_preview);
        Controls.Add(_list);
        Controls.Add(bar);
        _list.SelectedIndexChanged += (_, _) => ShowPreview();
        _remove.Click += (_, _) => { if (_list.SelectedIndex >= 0) _queue.Remove(_list.SelectedIndex); };
        _send.Click += (_, _) => send();
        _queue.Changed += Reload;
        FormClosed += (_, _) => { _queue.Changed -= Reload; _preview.Image?.Dispose(); Icon?.Dispose(); };   // verify V2
        Reload();
    }

    private void Reload()
    {
        if (IsDisposed) return;
        _list.Items.Clear();
        for (int i = 0; i < _queue.Items.Count; i++)
        {
            var it = _queue.Items[i];
            var c = string.IsNullOrWhiteSpace(it.Comment) ? "（一言なし）" : it.Comment.Replace("\r", " ").Replace("\n", " ");
            _list.Items.Add($"{i + 1}. {it.TakenAt:HH:mm:ss}  {c}  （{it.AppName}）");
        }
        if (_list.Items.Count > 0) _list.SelectedIndex = 0; else ShowPreview();
        _remove.Enabled = _send.Enabled = _list.Items.Count > 0;
    }

    private void ShowPreview()
    {
        _preview.Image?.Dispose(); _preview.Image = null;
        var i = _list.SelectedIndex;
        if (i < 0 || i >= _queue.Items.Count) return;
        try
        {
            using var fs = File.OpenRead(_queue.Items[i].ImagePath);   // ファイルを掴んだままにしない（消せるように）
            using var img = Image.FromStream(fs);
            _preview.Image = new Bitmap(img);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException) { }
    }
}

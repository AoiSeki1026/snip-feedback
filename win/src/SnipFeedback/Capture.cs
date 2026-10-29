using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using SnipFeedback.Core;

namespace SnipFeedback;

// 起動キーを押した瞬間の画面・前面のウィンドウ・見えているウィンドウの一覧（R-01・R-04・R-10）
internal sealed record Grab(Bitmap Screen, Rectangle Bounds, string AppName, string WindowTitle, DateTime At, IReadOnlyList<WindowInfo> Windows) : IDisposable
{
    public void Dispose() => Screen.Dispose();
}

internal static class Capture
{
    public static Grab Grab()
    {
        var at = DateTime.Now;
        var (app, title) = Describe(Native.GetForegroundWindow());
        var b = Screen.PrimaryScreen!.Bounds;
        var bmp = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppArgb);
        // Graphics.CopyFromScreen は CaptureBlt との組み合わせを受け付けないので BitBlt を直接呼ぶ。
        // CAPTUREBLT で重ねて表示されるウィンドウ（メニュー・通知・ツールチップ）も写す（T-02 で確認）
        using (var g = Graphics.FromImage(bmp))
        {
            var dst = g.GetHdc();
            var src = Native.GetDC(IntPtr.Zero);
            try { Native.BitBlt(dst, 0, 0, b.Width, b.Height, src, b.X, b.Y, Native.SRCCOPY | Native.CAPTUREBLT); }
            finally { Native.ReleaseDC(IntPtr.Zero, src); g.ReleaseHdc(dst); }
        }
        return new Grab(bmp, b, app, title, at, Windows(b));
    }

    // 見えているトップレベルのウィンドウを Z 順の手前から。非表示（cloaked）・最小化・自分のウィンドウは除く（design.md）
    private static List<WindowInfo> Windows(Rectangle screen)
    {
        var list = new List<WindowInfo>();
        var self = (uint)Environment.ProcessId;
        Native.EnumWindows((h, _) =>
        {
            if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
            Native.GetWindowThreadProcessId(h, out var pid);
            if (pid == self) return true;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out Native.RECT r, Marshal.SizeOf<Native.RECT>()) != 0) return true;
            var frame = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            frame.Offset(-screen.X, -screen.Y);
            var vis = WindowPicker.Visible(frame, new Rectangle(Point.Empty, screen.Size));
            if (vis == null) return true;
            var (app, title) = Describe(h);
            list.Add(new WindowInfo(h, vis.Value, app, title));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    // アプリ名（実行ファイル名）とウィンドウ名。取れなければ「不明」（R-10）
    public static (string App, string Title) Describe(IntPtr hwnd)
    {
        const string unknown = "不明";
        if (hwnd == IntPtr.Zero) return (unknown, unknown);
        var sb = new StringBuilder(512);
        Native.GetWindowText(hwnd, sb, sb.Capacity);
        var title = sb.Length > 0 ? sb.ToString() : unknown;
        var app = unknown;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        var h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h != IntPtr.Zero)
        {
            var name = new StringBuilder(1024); int size = name.Capacity;
            if (Native.QueryFullProcessImageName(h, 0, name, ref size)) app = Path.GetFileName(name.ToString());
            Native.CloseHandle(h);
        }
        return (app, title);
    }
}

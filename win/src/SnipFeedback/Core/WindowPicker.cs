using System.Drawing;

namespace SnipFeedback.Core;

// 起動キーを押した瞬間に見えていたウィンドウ（Z 順の手前から）。Bounds は影を除いた枠を画面に収めたもの
public sealed record WindowInfo(IntPtr Handle, Rectangle Bounds, string AppName, string Title);

// ある点の最前面のウィンドウを返す（R-04）。画面に依らない
public static class WindowPicker
{
    public static WindowInfo? Pick(IReadOnlyList<WindowInfo> windowsFrontToBack, Point p)
    {
        foreach (var w in windowsFrontToBack)
            if (w.Bounds.Contains(p)) return w;
        return null;
    }

    // ウィンドウの枠を画面に収める。小さすぎるもの（8 画素未満）は選ばせない
    public static Rectangle? Visible(Rectangle frame, Rectangle screen)
    {
        var r = Rectangle.Intersect(frame, screen);
        return r.Width >= SnipStateMachine.MinSize && r.Height >= SnipStateMachine.MinSize ? r : null;
    }
}

namespace SnipFeedback.Core;

// R-10・R-19: 切り取りに添えるアプリ名・ウィンドウ名と、URL を読むウィンドウの選び方（design.md「細部の決め」）
public static class SnipSource
{
    // 名前を付ける（URL を読む）ウィンドウ。ウィンドウの切り取り方ならクリックしたもの、四角なら範囲の中心のもの（r4）。無ければ null（R-10・R-19）
    public static WindowInfo? Window(WindowInfo? clicked, IReadOnlyList<WindowInfo> windows, System.Drawing.Rectangle cut) =>
        clicked ?? WindowPicker.Pick(windows, new System.Drawing.Point(cut.X + cut.Width / 2, cut.Y + cut.Height / 2));

    // アプリ名・ウィンドウ名。上の Window で決まらなければ（範囲の中心に何も無い）起動キーを押した時の前面のウィンドウ
    public static (string App, string Title) Choose(WindowInfo? clicked, IReadOnlyList<WindowInfo> windows, System.Drawing.Rectangle cut,
        string foregroundApp, string foregroundTitle)
    {
        var w = clicked ?? WindowPicker.Pick(windows, new System.Drawing.Point(cut.X + cut.Width / 2, cut.Y + cut.Height / 2));
        return w != null ? (w.AppName, w.Title) : (foregroundApp, foregroundTitle);
    }
}

// R-15: 日付が変わったことを知らせる（1 分ごとに呼ぶ）
public sealed class DayWatcher
{
    private DateTime _day;
    public DayWatcher(DateTime now) => _day = now.Date;

    public bool Changed(DateTime now)
    {
        if (now.Date == _day) return false;
        _day = now.Date;
        return true;
    }
}

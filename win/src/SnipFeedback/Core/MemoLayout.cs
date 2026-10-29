using System.Drawing;

namespace SnipFeedback.Core;

// 枠への一言の欄の高さ（行数）と置き場所（R-22、r13、H-M40）。画面から切り離して単体テストで確かめる
public static class MemoLayout
{
    public const int MinLines = 2;        // 開いたとき・畳んだとき
    public const int EditMaxLines = 8;    // 入力中に伸ばす上限（超えた分は欄の中でスクロール）
    public const int FullMaxLines = 12;   // ▼ で広げたときの上限

    // 入力中は打った分だけ（2〜8 行）。焦点が外れたら 2 行に畳み、▼ で広げていれば全文（2〜12 行）
    public static int Lines(bool focused, bool expanded, int contentLines)
    {
        if (focused) return Math.Clamp(contentLines, MinLines, EditMaxLines);
        return expanded ? Math.Clamp(contentLines, MinLines, FullMaxLines) : MinLines;
    }

    // 畳んだ 2 行に収まらない中身があるか（▼▲ を出すか）
    public static bool HasMore(int contentLines) => contentLines > MinLines;

    // 欄を置く場所。keep（今の位置）で重ならなければそのまま（入力で伸びるたびに動かない）。
    // 重なるなら枠の右→左→下→上の順に、obstacles（ほかの欄・全体の一言の欄・赤枠）と重ならない最初の位置。
    // どこも重なるなら重なりの面積が最も小さい位置（同じなら先の候補）。どの候補も画面の中へ押し戻してから比べる
    public static Rectangle Place(Rectangle box, Size size, Rectangle screen, IReadOnlyCollection<Rectangle> obstacles, int gap, Point? keep = null)
    {
        var candidates = new List<Point>();
        if (keep is { } k) candidates.Add(k);
        candidates.Add(new Point(box.Right + gap, box.Top));                // 右
        candidates.Add(new Point(box.Left - gap - size.Width, box.Top));    // 左
        candidates.Add(new Point(box.Left, box.Bottom + gap));              // 下
        candidates.Add(new Point(box.Left, box.Top - gap - size.Height));   // 上

        Rectangle best = default; long bestScore = long.MaxValue;
        foreach (var p in candidates)
        {
            var r = Fit(new Rectangle(p, size), screen);
            var score = Overlap(r, obstacles);
            if (score == 0) return r;
            if (score < bestScore) { best = r; bestScore = score; }
        }
        return best;
    }

    // 重なりの面積の合計（辺が接するだけは 0）
    public static long Overlap(Rectangle r, IEnumerable<Rectangle> obstacles)
    {
        long sum = 0;
        foreach (var o in obstacles)
        {
            var i = Rectangle.Intersect(r, o);
            if (i.Width > 0 && i.Height > 0) sum += (long)i.Width * i.Height;
        }
        return sum;
    }

    private static Rectangle Fit(Rectangle r, Rectangle screen)
    {
        var x = Math.Max(screen.Left, Math.Min(r.X, screen.Right - r.Width));
        var y = Math.Max(screen.Top, Math.Min(r.Y, screen.Bottom - r.Height));
        return new Rectangle(x, y, r.Width, r.Height);
    }
}

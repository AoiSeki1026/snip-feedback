using System.Drawing;
using SnipFeedback.Core;

namespace SnipFeedback.Tests;

// R-22（r13、H-M40）: 枠への一言の欄の高さ（行数）と置き場所
public class MemoLayoutTests
{
    private static readonly Rectangle Screen = new(0, 0, 1000, 800);
    private static readonly Size Memo = new(200, 40);

    [Theory]
    [InlineData(true, false, 1, 2)]     // 入力中: 2 行から
    [InlineData(true, false, 5, 5)]     // 打った分だけ伸びる
    [InlineData(true, false, 20, 8)]    // 最大 8 行
    [InlineData(true, true, 20, 8)]     // 入力中は広げた印に関係なく 8 行まで
    [InlineData(false, false, 20, 2)]   // 焦点が外れたら 2 行に畳む
    [InlineData(false, true, 5, 5)]     // ▼ で全文
    [InlineData(false, true, 30, 12)]   // 全文は上限 12 行
    [InlineData(false, true, 1, 2)]     // 短くても 2 行
    public void 行数(bool focused, bool expanded, int contentLines, int expected) =>
        Assert.Equal(expected, MemoLayout.Lines(focused, expanded, contentLines));

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void 畳んだ欄に_3_行目以降があるときだけ_開け閉めの印を出す(int contentLines, bool expected) =>
        Assert.Equal(expected, MemoLayout.HasMore(contentLines));

    [Fact]
    public void 何も無ければ枠の右に置く()
    {
        var box = new Rectangle(100, 100, 50, 50);
        Assert.Equal(new Rectangle(154, 100, 200, 40), MemoLayout.Place(box, Memo, Screen, Array.Empty<Rectangle>(), 4));
    }

    [Fact]
    public void 右が重なるなら左_左も重なるなら下_下も重なるなら上()
    {
        var box = new Rectangle(400, 300, 50, 50);
        var right = new Rectangle(454, 300, 200, 40);
        var left = new Rectangle(196, 300, 200, 40);
        var below = new Rectangle(400, 354, 200, 40);
        Assert.Equal(left, MemoLayout.Place(box, Memo, Screen, new[] { right }, 4));
        Assert.Equal(below, MemoLayout.Place(box, Memo, Screen, new[] { right, left }, 4));
        Assert.Equal(new Rectangle(400, 256, 200, 40), MemoLayout.Place(box, Memo, Screen, new[] { right, left, below }, 4));
    }

    [Fact]
    public void どこも重なるなら重なりが最も小さい位置()
    {
        var box = new Rectangle(400, 300, 50, 50);
        var obstacles = new[]
        {
            new Rectangle(454, 300, 200, 40),    // 右を全部ふさぐ
            new Rectangle(196, 300, 200, 40),    // 左を全部ふさぐ
            new Rectangle(400, 354, 200, 40),    // 下を全部ふさぐ
            new Rectangle(400, 256, 20, 40),     // 上は少しだけ
        };
        Assert.Equal(new Rectangle(400, 256, 200, 40), MemoLayout.Place(box, Memo, Screen, obstacles, 4));
    }

    [Fact]
    public void 画面の端では画面の中へ押し戻す()
    {
        var box = new Rectangle(900, 780, 50, 20);   // 右下の隅
        var r = MemoLayout.Place(box, Memo, Screen, Array.Empty<Rectangle>(), 4);
        Assert.True(Screen.Contains(r));
    }

    [Fact]
    public void 押し戻した右の候補が自分の枠に重なるなら_ほかの候補を選ぶ()
    {
        var box = new Rectangle(900, 100, 80, 50);   // 右に余地が無い
        var r = MemoLayout.Place(box, Memo, Screen, new[] { box }, 4);   // 赤枠も重ならない相手に含める
        Assert.Equal(new Rectangle(696, 100, 200, 40), r);              // 左
    }

    [Fact]
    public void 今の位置で重ならなければ動かさない()
    {
        var box = new Rectangle(100, 100, 50, 50);
        var keep = new Point(500, 500);
        Assert.Equal(new Rectangle(500, 500, 200, 40), MemoLayout.Place(box, Memo, Screen, Array.Empty<Rectangle>(), 4, keep));
    }

    [Fact]
    public void 今の位置で重なるなら右から探し直す()
    {
        var box = new Rectangle(100, 100, 50, 50);
        var keep = new Point(500, 500);
        var r = MemoLayout.Place(box, Memo, Screen, new[] { new Rectangle(520, 520, 10, 10) }, 4, keep);
        Assert.Equal(new Rectangle(154, 100, 200, 40), r);
    }

    [Fact]
    public void 今の位置が画面からはみ出すなら押し戻す()
    {
        var box = new Rectangle(100, 100, 50, 50);
        var r = MemoLayout.Place(box, new Size(200, 300), Screen, Array.Empty<Rectangle>(), 4, new Point(500, 700));   // 伸びて下にはみ出す
        Assert.Equal(new Rectangle(500, 500, 200, 300), r);
    }

    // verify-h-m40 T1: 左端・上端でも画面の中へ押し戻す
    [Fact]
    public void 左の候補が画面の左にはみ出すなら左端へ押し戻す()
    {
        var box = new Rectangle(10, 300, 50, 50);
        var r = MemoLayout.Place(box, Memo, Screen, new[] { new Rectangle(210, 300, 50, 40) }, 4);   // 右の候補（64〜264）の端だけをふさぐ。押し戻した左（0〜200）にはかからない
        Assert.Equal(new Rectangle(0, 300, 200, 40), r);
    }

    [Fact]
    public void 上の候補が画面の上にはみ出すなら上端へ押し戻す()
    {
        var box = new Rectangle(400, 10, 50, 50);
        var obstacles = new[] { new Rectangle(454, 10, 200, 40), new Rectangle(196, 10, 200, 40), new Rectangle(400, 64, 200, 40) };   // 右・左・下をふさぐ
        Assert.Equal(new Rectangle(400, 0, 200, 40), MemoLayout.Place(box, Memo, Screen, obstacles, 4));
    }

    // verify-h-m40 T1: 重なりが同じなら先の候補（右）
    [Fact]
    public void 重なりが同じなら先の候補を選ぶ()
    {
        var box = new Rectangle(400, 300, 50, 50);
        var obstacles = new[]
        {
            new Rectangle(454, 300, 10, 10),     // 右に少し
            new Rectangle(196, 300, 10, 10),     // 左にも同じだけ
            new Rectangle(400, 354, 200, 40),    // 下は全部
            new Rectangle(400, 256, 200, 40),    // 上も全部
        };
        Assert.Equal(new Rectangle(454, 300, 200, 40), MemoLayout.Place(box, Memo, Screen, obstacles, 4));
    }

    [Fact]
    public void 重なりの面積()
    {
        Assert.Equal(100, MemoLayout.Overlap(new Rectangle(0, 0, 20, 20), new[] { new Rectangle(10, 10, 20, 20) }));
        Assert.Equal(0, MemoLayout.Overlap(new Rectangle(0, 0, 20, 20), new[] { new Rectangle(20, 0, 20, 20) }));   // 辺が接するだけは重ならない
        Assert.Equal(100 + 400, MemoLayout.Overlap(new Rectangle(0, 0, 20, 20), new[] { new Rectangle(10, 10, 20, 20), new Rectangle(0, 0, 20, 20) }));
    }
}

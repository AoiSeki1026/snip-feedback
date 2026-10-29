namespace SnipFeedback.Tests;

// H-M33: アプリのアイコン（app.ico）と、トレイの件数の描き方
public class IconTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    public void 埋め込んだアイコンにその大きさの絵がある(int size)
    {
        using var icon = Program.AppIcon(size);
        Assert.Equal(size, icon.Width);
        Assert.Equal(size, icon.Height);
    }

    [Fact]
    public void エクスプローラー用に256の絵も入っている()
    {
        // ICO の見出し: 予約 2・種類 2・枚数 2、続いて 16 バイトずつの項目（幅 0 は 256 の意味）
        using var s = typeof(Program).Assembly.GetManifestResourceStream("SnipFeedback.app.ico")!;
        using var r = new BinaryReader(s);
        r.ReadUInt16(); Assert.Equal(1, r.ReadUInt16());
        int count = r.ReadUInt16();
        var widths = new List<int>();
        for (int i = 0; i < count; i++) { var w = r.ReadByte(); widths.Add(w == 0 ? 256 : w); r.ReadBytes(15); }
        Assert.Contains(256, widths);
        Assert.Contains(16, widths);
    }

    [Fact]
    public void アイコンの四隅は透明で中は緑の地()
    {
        using var bmp = Program.AppIcon(48).ToBitmap();
        Assert.True(bmp.GetPixel(0, 0).A < 16);   // 縮めたときのにじみ（1 程度）は許す
        Assert.True(bmp.GetPixel(47, 47).A < 16);
        var c = bmp.GetPixel(4, 24);   // 左の縁の近く（緑の地）
        Assert.Equal(255, c.A);
        Assert.True(c.G > c.R + 25 && c.G > c.B, $"緑の地のはず: {c}");
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void 件数があるときだけ右下に赤い丸を描く(int px)
    {
        // 丸の中で数字の字面を避けた点（丸の左寄り・縦は中ほど）
        var d = px * 0.68f;
        int x = (int)(px - d + d * 0.2f), y = (int)(px - d / 2);
        using (var none = TrayApp.DrawTrayIcon(px, 0))
            Assert.False(IsRed(none.GetPixel(x, y)), "0 件では丸を描かない");
        using (var three = TrayApp.DrawTrayIcon(px, 3))
            Assert.True(IsRed(three.GetPixel(x, y)), $"3 件の丸: {three.GetPixel(x, y)}");
        using (var many = TrayApp.DrawTrayIcon(px, 150))   // 99 で止める
        using (var n99 = TrayApp.DrawTrayIcon(px, 99))
            Assert.True(Same(many, n99), "100 件以上は 99 と同じ絵");
    }

    // verify V1: 下地はアプリの絵、丸の中には白い数字がある
    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    public void 下地はアプリの絵で丸の中に白い数字がある(int px)
    {
        using var plain = new Bitmap(px, px);
        using (var g = Graphics.FromImage(plain))
        using (var app = Program.AppIcon(px)) g.DrawIcon(app, new Rectangle(0, 0, px, px));
        using (var none = TrayApp.DrawTrayIcon(px, 0))
            Assert.True(Same(none, plain), "0 件はアプリの絵そのまま（縁が半画素ずれない）");

        var d = px * 0.68f;
        using var three = TrayApp.DrawTrayIcon(px, 3);
        using var eight = TrayApp.DrawTrayIcon(px, 8);
        int white = 0;
        for (int y = (int)(px - d); y < px; y++)
            for (int x = (int)(px - d); x < px; x++)
            {
                var c = three.GetPixel(x, y);
                var cx = px - d / 2f; var cy = px - d / 2f;
                if ((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy) < d * d / 9f && c.R > 200 && c.G > 200 && c.B > 200) white++;
            }
        Assert.True(white > 0, "丸の中ほどに白い字の画素があるはず");
        Assert.False(Same(three, eight), "数字が違えば絵も違う");
    }

    [Fact]
    public void exeのアイコンも同じ絵()
    {
        var exe = Path.ChangeExtension(typeof(Program).Assembly.Location, ".exe");
        Assert.True(File.Exists(exe), exe);
        using var icon = Icon.ExtractAssociatedIcon(exe)!;
        using var bmp = icon.ToBitmap();
        var c = bmp.GetPixel(3, bmp.Height / 2);   // 左の縁の近く（緑の地）
        Assert.True(c.A > 200 && c.G > c.R + 25 && c.G > c.B, $"緑の地のはず: {c}");
    }

    private static bool IsRed(Color c) => c.A == 255 && c.R > 180 && c.G < 90 && c.B < 90;

    private static bool Same(Bitmap a, Bitmap b, int tol = 0)
    {
        if (a.Size != b.Size) return false;
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
            {
                Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                if (Math.Abs(p.A - q.A) > tol || Math.Abs(p.R - q.R) > tol || Math.Abs(p.G - q.G) > tol || Math.Abs(p.B - q.B) > tol) return false;
            }
        return true;
    }
}

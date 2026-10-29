using System.Drawing;
using System.Drawing.Imaging;
using SnipFeedback.Core;

namespace SnipFeedback.Tests;

// N-02・AC-12（切り出しが画素単位で一致）、R-08（赤枠の焼き込み）
public class SnipImageTests
{
    private static Bitmap Pattern(int w, int h)
    {
        var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                b.SetPixel(x, y, Color.FromArgb(255, x % 256, y % 256, (x * 7 + y * 3) % 256));
        return b;
    }

    [Fact]
    public void 切り出しは元の画面と画素単位で一致する()
    {
        using var screen = Pattern(300, 200);
        var area = new Rectangle(37, 21, 101, 63);
        using var crop = SnipImage.Crop(screen, area);
        Assert.Equal(area.Size, crop.Size);
        for (int y = 0; y < area.Height; y += 7)
            for (int x = 0; x < area.Width; x += 5)
                Assert.Equal(screen.GetPixel(area.X + x, area.Y + y), crop.GetPixel(x, y));
        Assert.Equal(screen.GetPixel(area.Right - 1, area.Bottom - 1), crop.GetPixel(area.Width - 1, area.Height - 1));
    }

    [Fact]
    public void 画面からはみ出す範囲は画面に収める()
    {
        using var screen = Pattern(100, 100);
        using var crop = SnipImage.Crop(screen, new Rectangle(80, 90, 50, 50));
        Assert.Equal(new Size(20, 10), crop.Size);
    }

    [Fact]
    public void 赤枠を焼き込み_枠の外は変えない()
    {
        using var screen = new Bitmap(200, 200, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(screen)) g.Clear(Color.White);
        using var baked = SnipImage.Bake(screen, new Rectangle(0, 0, 200, 200), new[] { new SnipBox(1, 50, 60, 80, 70, "") }, 1f);
        Assert.Equal(SnipImage.Red.ToArgb(), baked.GetPixel(90, 130).ToArgb());   // 下の辺
        Assert.Equal(SnipImage.Red.ToArgb(), baked.GetPixel(130, 100).ToArgb());  // 右の辺
        Assert.Equal(Color.White.ToArgb(), baked.GetPixel(90, 100).ToArgb());     // 枠の中
        Assert.Equal(Color.White.ToArgb(), baked.GetPixel(190, 190).ToArgb());    // 枠の外
    }
}

using System.Drawing;
using SnipFeedback.Core;

namespace SnipFeedback.Tests;

// R-04（ウィンドウの切り取り方）
public class WindowPickerTests
{
    private static WindowInfo W(int id, int x, int y, int w, int h) => new((IntPtr)id, new Rectangle(x, y, w, h), "a.exe", "w" + id);

    [Fact]
    public void 重なっていれば手前のウィンドウを選ぶ()
    {
        var list = new[] { W(1, 100, 100, 200, 200), W(2, 0, 0, 1000, 800) };   // 手前から
        Assert.Equal((IntPtr)1, WindowPicker.Pick(list, new Point(150, 150))!.Handle);
        Assert.Equal((IntPtr)2, WindowPicker.Pick(list, new Point(50, 50))!.Handle);
        Assert.Null(WindowPicker.Pick(list, new Point(2000, 50)));
    }

    [Fact]
    public void 枠は画面に収め_8画素未満は選ばせない()
    {
        var screen = new Rectangle(0, 0, 3840, 2160);
        Assert.Equal(new Rectangle(0, 10, 300, 200), WindowPicker.Visible(new Rectangle(-100, 10, 400, 200), screen));
        Assert.Null(WindowPicker.Visible(new Rectangle(3835, 10, 400, 200), screen));
        Assert.Null(WindowPicker.Visible(new Rectangle(5000, 10, 400, 200), screen));
    }
}

using System.Reflection;
using SnipFeedback.Core;

namespace SnipFeedback.Tests;

// verify-h-m40 F1: 描き始めに欄を隠したとき（H-M36）や、右クリックで焦点のある枠を消したときに、
// 焦点が残りの欄へ順に移っても、触っていない枠の欄を動かさない（R-22「今の位置で重ならなければ動かさない」）。
// 本物の OverlayForm を画面の外に出して確かめる
public class MemoFocusTests
{
    private static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(o)!;
    private static void Call(object o, string name) => o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(o, null);

    // STA のスレッドで編集画面を開き、body を走らせて閉じる
    private static void OnEditing(Action<OverlayForm, TextBox, Dictionary<Sketch.Item, TextBox>> body)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            using var bmp = new Bitmap(1600, 1000);
            var grab = new Grab(bmp, new Rectangle(-4000, -3000, 1600, 1000), "", "", DateTime.Now, Array.Empty<WindowInfo>());
            using var f = new OverlayForm(grab, SnipMode.Rect, null);
            f.Shown += (_, _) =>
            {
                try
                {
                    f.EnterEditing(new Rectangle(100, 100, 1200, 800));
                    body(f, Field<TextBox>(f, "_comment"), Field<Dictionary<Sketch.Item, TextBox>>(f, "_boxText"));
                }
                catch (Exception ex) { error = ex; }
                finally { f.Close(); }
            };
            Application.Run(f);
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        Assert.True(t.Join(TimeSpan.FromSeconds(30)), "編集画面が閉じない");
        if (error != null) throw new Xunit.Sdk.XunitException(error.ToString());
    }

    private static string Lines(int n) => string.Join("\r\n", Enumerable.Range(1, n).Select(i => "長い一言の行 " + i));

    [Fact]
    public void F1_描き始めに欄を隠しても_触っていない枠の欄は動かない()
    {
        OnEditing((f, comment, boxText) =>
        {
            f.AddBox(new Rectangle(400, 200, 300, 150));
            var m1 = boxText.Values.First();
            m1.Text = Lines(8);
            f.AddBox(new Rectangle(400, 360, 300, 150));
            var m2 = boxText.Values.Last();
            comment.Focus();
            var b1 = m1.Bounds; var b2 = m2.Bounds;
            Call(f, "HideTexts");    // 全体の一言の欄に焦点があるまま描き始める
            Call(f, "ShowTexts");
            Assert.Equal(b1, m1.Bounds);
            Assert.Equal(b2, m2.Bounds);
            Assert.True(comment.Focused);
        });
    }

    [Fact]
    public void F1_広げた欄は描き始めても畳まれない()
    {
        OnEditing((f, comment, boxText) =>
        {
            f.AddBox(new Rectangle(400, 200, 300, 150));
            var m1 = boxText.Values.First();
            m1.Text = Lines(8);
            comment.Focus();
            var toggle = Field<Dictionary<TextBox, Label>>(f, "_memoToggle")[m1];
            typeof(Control).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(toggle, new object[] { EventArgs.Empty });   // ▼
            var opened = m1.Bounds;
            Call(f, "HideTexts");
            Call(f, "ShowTexts");
            Assert.Equal(opened, m1.Bounds);
            Assert.Equal("▲", toggle.Text);
        });
    }

    [Fact]
    public void F1_焦点のある枠を右クリックで消しても_ほかの枠の欄は動かない()
    {
        OnEditing((f, comment, boxText) =>
        {
            f.AddBox(new Rectangle(400, 200, 300, 150));
            var m1 = boxText.Values.First();
            m1.Text = Lines(8);
            f.AddBox(new Rectangle(400, 360, 300, 150));   // m2 に焦点
            var b1 = boxText.Values.First().Bounds;
            Call(f, "RemoveLast");
            Assert.Equal(b1, m1.Bounds);
            Assert.True(comment.Focused);
        });
    }

    [Fact]
    public void 枠の欄に縦のスクロールバーを出さない()
    {
        OnEditing((f, comment, boxText) =>
        {
            f.AddBox(new Rectangle(400, 200, 300, 150));
            Assert.Equal(ScrollBars.None, boxText.Values.First().ScrollBars);
            Assert.Equal(ScrollBars.Vertical, comment.ScrollBars);   // 全体の一言の欄は今までどおり
        });
    }
}

using System.Drawing;
using SnipFeedback.Core;

namespace SnipFeedback.Tests;

// 最終の verify（docs/verify/20261012_snip-v2_verify-c.md）の指摘への対応
public class VerifyCFixTests
{
    private static SnipItem Item(string path, string comment = "新") => new(path, comment, new List<SnipBox>(), "a.exe", "w", new DateTime(2026, 10, 12, 9, 0, 0));

    private static string OriginalQueue(TempDir d, string png) =>
        "[{\"ImagePath\":" + System.Text.Json.JsonSerializer.Serialize(png) + ",\"Comment\":\"元\",\"Boxes\":[],\"AppName\":\"a\",\"WindowTitle\":\"w\",\"TakenAt\":\"2026-10-01T09:00:00\"}]";

    [Fact]
    public void V21_起動時に読めなかったら自動削除を止める()
    {
        using var d = new TempDir();
        var png = d.File("old.png"); File.WriteAllBytes(png, new byte[] { 1 });
        File.WriteAllText(d.File("queue.json"), OriginalQueue(d, png));
        SnipQueue q;
        using (new FileStream(d.File("queue.json"), FileMode.Open, FileAccess.Read, FileShare.None)) q = new SnipQueue(d.File("queue.json"));
        Assert.NotNull(q.LoadError);
        Assert.False(q.CanCleanup);   // 一覧に何があるか分からないので、古い画像を消さない
        using var ok = new TempDir();
        Assert.True(new SnipQueue(ok.File("queue.json")).CanCleanup);
    }

    [Fact]
    public void V21_読めなかった元の一覧は上書きする前に複製して残す()
    {
        using var d = new TempDir();
        var png = d.File("old.png"); File.WriteAllBytes(png, new byte[] { 1 });
        var original = OriginalQueue(d, png);
        File.WriteAllText(d.File("queue.json"), original);
        File.SetLastWriteTime(d.File("queue.json"), DateTime.Now.AddDays(-30));
        SnipQueue q;
        using (new FileStream(d.File("queue.json"), FileMode.Open, FileAccess.Read, FileShare.None)) q = new SnipQueue(d.File("queue.json"));
        var add = d.File("new.png"); File.WriteAllBytes(add, new byte[] { 1 });
        q.Add(Item(add));
        var kept = Directory.GetFiles(d.Path, "queue.json.unread-*");
        Assert.Single(kept);
        Assert.Equal(original, File.ReadAllText(kept[0]));
        Assert.Single(new SnipQueue(d.File("queue.json")).Items);
        // 控えは残した日から数える（元の更新日時が古くても、すぐには消えない）
        Cleanup.Run(d.Path, DateTime.Today, 7, Array.Empty<string>());
        Assert.Single(Directory.GetFiles(d.Path, "queue.json.unread-*"));
    }

    [Fact]
    public void V21_元の一覧をまだ開けなければ上書きせずに断る_E02()
    {
        using var d = new TempDir();
        var png = d.File("old.png"); File.WriteAllBytes(png, new byte[] { 1 });
        var original = OriginalQueue(d, png);
        File.WriteAllText(d.File("queue.json"), original);
        using (new FileStream(d.File("queue.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var q = new SnipQueue(d.File("queue.json"));
            var add = d.File("new.png"); File.WriteAllBytes(add, new byte[] { 1 });
            Assert.ThrowsAny<IOException>(() => q.Add(Item(add)));
            Assert.Empty(q.Items);
        }
        Assert.Equal(original, File.ReadAllText(d.File("queue.json")));
    }

    [Fact]
    public void V24_退避ファイルの境界_ちょうど保存日数は残し_1日過ぎたら消す()
    {
        using var d = new TempDir();
        var today = new DateTime(2026, 10, 18);
        var edge = d.File("queue.json.broken-20261011090000");
        var over = d.File("queue.json.broken-20261010090000");
        File.WriteAllText(edge, "x"); File.WriteAllText(over, "x");
        File.SetLastWriteTime(edge, new DateTime(2026, 10, 11, 9, 0, 0));   // 7 日前
        File.SetLastWriteTime(over, new DateTime(2026, 10, 10, 9, 0, 0));   // 8 日前
        Cleanup.Run(d.Path, today, 7, Array.Empty<string>());
        Assert.True(File.Exists(edge));
        Assert.False(File.Exists(over));
    }

    [Fact]
    public void V24_ウィンドウの切り取り方では範囲の中心よりクリックしたウィンドウを優先する()
    {
        var clicked = new WindowInfo((IntPtr)1, new Rectangle(0, 0, 300, 300), "code.exe", "VS Code");
        var center = new WindowInfo((IntPtr)2, new Rectangle(0, 0, 1000, 1000), "claude.exe", "Claude");
        // 手前の一覧では center が先にあり、範囲の中心 (500,500) も center の上
        Assert.Equal(("code.exe", "VS Code"), SnipSource.Choose(clicked, new[] { center, clicked }, new Rectangle(0, 0, 1000, 1000), "explorer.exe", "Program Manager"));
    }
}

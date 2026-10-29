using SnipFeedback.Core;

namespace SnipFeedback.Tests;

// R-11・R-12・R-16・E-02（ためる一覧と queue.json）
public class QueueTests
{
    private static SnipItem Item(string path) =>
        new(path, "一言", new List<SnipBox> { new(1, 1, 2, 3, 4, "枠") }, "a.exe", "w", new DateTime(2026, 10, 10, 9, 0, 0));

    [Fact]
    public void 加えると_queue_json_に書き_起動し直すと復元される_R16()
    {
        using var d = new TempDir();
        var png = d.File("1.png"); File.WriteAllBytes(png, new byte[] { 1 });
        var q = new SnipQueue(d.File("queue.json"));
        q.Add(Item(png));
        var q2 = new SnipQueue(d.File("queue.json"));
        Assert.Single(q2.Items);
        Assert.Equal(png, q2.Items[0].ImagePath);
        Assert.Equal("枠", q2.Items[0].Boxes[0].Comment);
        Assert.Equal(new DateTime(2026, 10, 10, 9, 0, 0), q2.Items[0].TakenAt);
    }

    [Fact]
    public void 画像が無くなった項目は復元しない()
    {
        using var d = new TempDir();
        var png = d.File("1.png"); File.WriteAllBytes(png, new byte[] { 1 });
        new SnipQueue(d.File("queue.json")).Add(Item(png));
        File.Delete(png);
        Assert.Empty(new SnipQueue(d.File("queue.json")).Items);
    }

    [Fact]
    public void 一件消すと画像も消える_R12()
    {
        using var d = new TempDir();
        var a = d.File("a.png"); var b = d.File("b.png");
        File.WriteAllBytes(a, new byte[] { 1 }); File.WriteAllBytes(b, new byte[] { 1 });
        var q = new SnipQueue(d.File("queue.json"));
        q.Add(Item(a)); q.Add(Item(b));
        q.Remove(0);
        Assert.False(File.Exists(a));
        Assert.True(File.Exists(b));
        Assert.Single(new SnipQueue(d.File("queue.json")).Items);
    }

    [Fact]
    public void 送った項目を外しても画像は残す_送った後の7日間()
    {
        using var d = new TempDir();
        var a = d.File("a.png"); File.WriteAllBytes(a, new byte[] { 1 });
        var q = new SnipQueue(d.File("queue.json"));
        q.Add(Item(a));
        q.RemoveSent(q.Items.ToList());
        Assert.Empty(new SnipQueue(d.File("queue.json")).Items);
        Assert.True(File.Exists(a));
    }

    [Fact]
    public void queue_json_に書けないときは加えず例外_E02()
    {
        using var d = new TempDir();
        var a = d.File("a.png"); File.WriteAllBytes(a, new byte[] { 1 });
        var dir = d.File("queue.json");   // 同じ名前のフォルダがあると書けない
        Directory.CreateDirectory(dir);
        var q = new SnipQueue(dir);
        Assert.ThrowsAny<IOException>(() => q.Add(Item(a)));
        Assert.Empty(q.Items);
    }

    [Fact]
    public void 壊れた_queue_json_は空として読み_元は退避する()
    {
        using var d = new TempDir();
        File.WriteAllText(d.File("queue.json"), "{壊れ");
        var q = new SnipQueue(d.File("queue.json"));
        Assert.Empty(q.Items);
        Assert.Single(Directory.GetFiles(d.Path, "queue.json.broken-*"));
    }
}

using SnipFeedback.Core;

namespace SnipFeedback.Tests;

// T-09b（H-M37）: verify-d の V27（一覧を読めなかったときの複製まわりのテストの穴）と V28（控えにだけ載る画像）
public class T09bTests
{
    private static SnipItem Item(string path) => new(path, "新", new List<SnipBox>(), "a", "w", new DateTime(2026, 10, 19, 9, 0, 0));

    private static string QueueJson(params string[] pngs) =>
        "[" + string.Join(",", pngs.Select(p => "{\"ImagePath\":" + System.Text.Json.JsonSerializer.Serialize(p) +
            ",\"Comment\":\"元\",\"Boxes\":[],\"AppName\":\"a\",\"WindowTitle\":\"w\",\"TakenAt\":\"2026-10-01T09:00:00\"}")) + "]";

    // N3: 壊れていて、退避（Move）もできなかったとき → 印を立て、上書き前に複製する
    [Fact]
    public void V27_N3_壊れていて退避もできなければ_上書きの前に元を複製する()
    {
        using var d = new TempDir();
        File.WriteAllText(d.File("queue.json"), "{壊れ");
        SnipQueue q;
        using (new FileStream(d.File("queue.json"), FileMode.Open, FileAccess.Read, FileShare.Read))   // 読めるが動かせない
            q = new SnipQueue(d.File("queue.json"));
        Assert.NotNull(q.LoadError);
        Assert.Empty(Directory.GetFiles(d.Path, "queue.json.broken-*"));
        var png = d.File("a.png"); File.WriteAllBytes(png, new byte[] { 1 });
        q.Add(Item(png));
        var kept = Directory.GetFiles(d.Path, "queue.json.unread-*");
        Assert.Single(kept);
        Assert.Equal("{壊れ", File.ReadAllText(kept[0]));
    }

    // N5: 複製に失敗したら、一覧を書けるときでも書かずに断る。
    // 複製先と同じ名前のファイルを先に置いて、複製だけを失敗させる（File.Copy は上書きしない）
    [Fact]
    public void V27_N5_複製できなければ_書けるときでも書かずに断る()
    {
        using var d = new TempDir();
        var original = QueueJson(d.File("old.png"));
        File.WriteAllText(d.File("old.png"), "x");
        File.WriteAllText(d.File("queue.json"), original);
        SnipQueue q;
        using (new FileStream(d.File("queue.json"), FileMode.Open, FileAccess.Read, FileShare.None)) q = new SnipQueue(d.File("queue.json"));
        Assert.NotNull(q.LoadError);
        var now = DateTime.Now;
        for (int i = -1; i < 30; i++)   // この先 30 秒ぶんの控えの名前をふさぐ
            File.WriteAllText(d.File("queue.json.unread-" + now.AddSeconds(i).ToString("yyyyMMddHHmmss")), "ふさぎ");
        var png = d.File("a.png"); File.WriteAllBytes(png, new byte[] { 1 });
        Assert.ThrowsAny<IOException>(() => q.Add(Item(png)));
        Assert.Empty(q.Items);
        Assert.Equal(original, File.ReadAllText(d.File("queue.json")));   // 元の一覧は上書きされていない
    }

    // N7: 控え（unread-*）も書いた日から keepDays を過ぎたら消す。ちょうどは残す
    [Fact]
    public void V27_N7_控えの境界()
    {
        using var d = new TempDir();
        var edge = d.File("queue.json.unread-20261011090000");
        var over = d.File("queue.json.unread-20261010090000");
        File.WriteAllText(edge, "[]"); File.WriteAllText(over, "[]");
        File.SetLastWriteTime(edge, new DateTime(2026, 10, 11, 9, 0, 0));
        File.SetLastWriteTime(over, new DateTime(2026, 10, 10, 9, 0, 0));
        Cleanup.Run(d.Path, new DateTime(2026, 10, 18), 7, Array.Empty<string>());
        Assert.True(File.Exists(edge));
        Assert.False(File.Exists(over));
    }

    // N8: 同じ起動の中で何回加えても、控えは 1 つ
    [Fact]
    public void V27_N8_控えは一度だけ作る()
    {
        using var d = new TempDir();
        File.WriteAllText(d.File("queue.json"), QueueJson(d.File("old.png")));
        SnipQueue q;
        using (new FileStream(d.File("queue.json"), FileMode.Open, FileAccess.Read, FileShare.None)) q = new SnipQueue(d.File("queue.json"));
        for (int i = 0; i < 3; i++)
        {
            var p = d.File($"{i}.png"); File.WriteAllBytes(p, new byte[] { 1 });
            q.Add(Item(p));
        }
        Assert.Single(Directory.GetFiles(d.Path, "queue.json.unread-*"));
        Assert.Equal(3, new SnipQueue(d.File("queue.json")).Items.Count);
    }

    // V28: 控え（unread-*・broken-*）に載っている画像は、控えがある間は自動削除で消さない
    [Fact]
    public void V28_控えに載っている画像は控えがある間は消さない()
    {
        using var d = new TempDir();
        Directory.CreateDirectory(d.File("20261001"));
        var inUnread = d.File("20261001", "090000-000.png");
        var inBroken = d.File("20261001", "090000-001.png");
        var orphan = d.File("20261001", "090000-002.png");
        foreach (var p in new[] { inUnread, inBroken, orphan }) File.WriteAllBytes(p, new byte[] { 1 });
        File.WriteAllText(d.File("queue.json.unread-20261018080000"), QueueJson(inUnread));
        File.WriteAllText(d.File("queue.json.broken-20261018080000"), QueueJson(inBroken)[..^5]);   // 壊れていても読める範囲から拾う
        var keep = Cleanup.BackupReferences(d.Path, DateTime.Today, 7, out _);
        Cleanup.Run(d.Path, new DateTime(2026, 10, 18), 7, keep);
        Assert.True(File.Exists(inUnread));
        Assert.True(File.Exists(inBroken));
        Assert.False(File.Exists(orphan));
    }

    // verify T1: 控えにおかしなパス（空白だけ・NUL・長すぎる・相対）が書かれていても落ちず、そのパスは無視する
    [Fact]
    public void T1_控えのおかしなパスでは落ちない()
    {
        using var d = new TempDir();
        Directory.CreateDirectory(d.File("20261001"));
        var orphan = d.File("20261001", "090000-000.png");
        File.WriteAllBytes(orphan, new byte[] { 1 });
        var bad = new[] { "   ", "C:\\a\u0000b.png", "C:\\" + new string('x', 40000) + ".png", "rel\\x.png" };
        File.WriteAllText(d.File("queue.json.unread-20261018080000"),
            "[" + string.Join(",", bad.Select(b => "{\"ImagePath\":" + System.Text.Json.JsonSerializer.Serialize(b) + "}")) + "]");   // 正しい JSON として書く（パスの検査まで届くように）
        var refs = Cleanup.BackupReferences(d.Path, DateTime.Today, 7, out var complete);
        Assert.True(complete);
        Assert.Empty(refs);                                                        // 使えるパスだけ返す
        Cleanup.Run(d.Path, new DateTime(2026, 10, 18), 7, new[] { "   ", "C:\\a\u0000b.png" });   // keep におかしな値が来ても落ちない
        Assert.False(File.Exists(orphan));
    }

    // verify T2: 読めない控えがあれば complete=false（呼び出し側はその回の自動削除を止める）
    [Fact]
    public void T2_読めない控えがあれば知らせる()
    {
        using var d = new TempDir();
        File.WriteAllText(d.File("queue.json.unread-20261018080000"), "[]");
        using (new FileStream(d.File("queue.json.unread-20261018080000"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Cleanup.BackupReferences(d.Path, DateTime.Today, 7, out var complete);
            Assert.False(complete);
        }
        Cleanup.BackupReferences(d.Path, DateTime.Today, 7, out var ok);
        Assert.True(ok);
    }

    // verify-b U1: 期限を過ぎた控えは、読めなくても止める理由にしない（Run がその回に消す。止まり続けて溜まらないように）
    [Fact]
    public void U1_期限を過ぎた読めない控えでは止めない()
    {
        using var d = new TempDir();
        var old = d.File("queue.json.unread-20261001080000");
        File.WriteAllText(old, "[]");
        File.SetLastWriteTime(old, DateTime.Today.AddDays(-8));
        using (new FileStream(old, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Cleanup.BackupReferences(d.Path, DateTime.Today, 7, out var complete);
            Assert.True(complete);
        }
    }

    // verify-c V1: ちょうど keepDays の控えは期限内（読めなければ止める）。> を >= にすると、控えにだけ載る画像が消える
    [Fact]
    public void V1_ちょうど保存日数の読めない控えでは止める()
    {
        using var d = new TempDir();
        var edge = d.File("queue.json.unread-20261001080000");
        File.WriteAllText(edge, "[]");
        File.SetLastWriteTime(edge, DateTime.Today.AddDays(-7));
        using (new FileStream(edge, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Cleanup.BackupReferences(d.Path, DateTime.Today, 7, out var complete);
            Assert.False(complete);
        }
    }

    [Fact]
    public void V28_控えが無ければ拾うものは無い()
    {
        using var d = new TempDir();
        Assert.Empty(Cleanup.BackupReferences(d.Path, DateTime.Today, 7, out _));
        Assert.Empty(Cleanup.BackupReferences(@"C:\no\such\dir-snipfb", DateTime.Today, 7, out _));
    }
}

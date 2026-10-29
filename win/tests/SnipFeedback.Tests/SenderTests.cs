using SnipFeedback.Core;

namespace SnipFeedback.Tests;

// R-13（r7: 送る文の .md ＋ 画像ファイルをクリップボードへ）・E-03・E-06（送信）
public class SenderTests
{
    private sealed class FakeClipboard : IClipboardWriter
    {
        public int FailTimes; public int Calls; public List<string>? Files;
        public bool TrySetFiles(IReadOnlyList<string> paths) { Calls++; if (Calls <= FailTimes) return false; Files = paths.ToList(); return true; }
    }

    private static (SnipQueue q, TempDir d) QueueWith(int n)
    {
        var d = new TempDir();
        var q = new SnipQueue(d.File("queue.json"));
        for (int i = 0; i < n; i++)
        {
            var p = d.File($"{i}.png"); File.WriteAllBytes(p, new byte[] { 1 });
            q.Add(new SnipItem(p, $"一言{i}", new List<SnipBox>(), "a.exe", "w", new DateTime(2026, 10, 10, 9, 0, i)));
        }
        return (q, d);
    }

    [Fact]
    public void 空なら何もせず知らせる_E06()
    {
        var (q, d) = QueueWith(0); using var _ = d;
        var cb = new FakeClipboard();
        var r = new Sender(cb, d.Path, _ => { }).Send(q, new DateTime(2026, 10, 10, 10, 0, 0));
        Assert.Equal(SendOutcome.NothingToSend, r.Outcome);
        Assert.Equal(0, cb.Calls);
        Assert.False(Directory.Exists(d.File("20261010")));   // .md も書かない
    }

    [Fact]
    public void 送る文を_send_md_に書き_mdと画像をファイルとしてクリップボードへ入れ_一覧を空にする_r7()
    {
        var (q, d) = QueueWith(2); using var _ = d;
        var pngs = q.Items.Select(i => i.ImagePath).ToList();
        var cb = new FakeClipboard();
        var r = new Sender(cb, d.Path, _ => { }).Send(q, new DateTime(2026, 10, 10, 10, 5, 7));
        var md = Path.Combine(d.Path, "20261010", "send-100507-000.md");
        Assert.Equal(SendOutcome.Sent, r.Outcome);
        Assert.Equal(md, r.FallbackPath);
        Assert.Equal(new[] { md }.Concat(pngs), cb.Files);   // .md が先、画像は送る文の番号の順
        Assert.StartsWith("切り取り 2 件", File.ReadAllText(md));
        Assert.Empty(q.Items);
    }

    [Fact]
    public void 失敗しても3回目までに書ければ送れる_0_2秒おき_E03()
    {
        var (q, d) = QueueWith(1); using var _ = d;
        var cb = new FakeClipboard { FailTimes = 2 };
        var waits = new List<int>();
        var r = new Sender(cb, d.Path, waits.Add).Send(q, DateTime.Now);
        Assert.Equal(SendOutcome.Sent, r.Outcome);
        Assert.Equal(3, cb.Calls);
        Assert.Equal(new[] { 200, 200 }, waits);
    }

    [Fact]
    public void 三回とも失敗したら_send_md_の場所を返し_一覧は残す_E03()
    {
        var (q, d) = QueueWith(1); using var _ = d;
        var cb = new FakeClipboard { FailTimes = 3 };
        var r = new Sender(cb, d.Path, _ => { }).Send(q, new DateTime(2026, 10, 10, 10, 5, 7));
        Assert.Equal(SendOutcome.SavedToFile, r.Outcome);
        Assert.Equal(Path.Combine(d.Path, "20261010", "send-100507-000.md"), r.FallbackPath);
        Assert.StartsWith("切り取り 1 件", File.ReadAllText(r.FallbackPath!));
        Assert.Single(q.Items);
    }
}

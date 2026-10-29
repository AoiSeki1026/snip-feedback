using SnipFeedback.Core;

namespace SnipFeedback.Tests;

// R-21・E-08（r9）: 送った単位の履歴から入れ直す
public class HistoryTests
{
    private sealed class FakeClipboard : IClipboardWriter
    {
        public int FailTimes; public int Calls; public List<string>? Files;
        public bool TrySetFiles(IReadOnlyList<string> paths) { Calls++; if (Calls <= FailTimes) return false; Files = paths.ToList(); return true; }
    }

    private static string Png(TempDir d, string day, string name)
    {
        Directory.CreateDirectory(d.File(day));
        var p = Path.Combine(d.Path, day, name); File.WriteAllBytes(p, new byte[] { 1 }); return p;
    }

    // 実際の送信（Sender.Send）で .md を書く。送る文の形（R-13）を変えたら、ここで気付く
    private static string SendReal(TempDir d, DateTime at, params (string png, string comment)[] items)
    {
        var q = new SnipQueue(d.File("queue.json"));
        foreach (var (png, comment) in items)
            q.Add(new SnipItem(png, comment, new List<SnipBox> { new(1, 0, 0, 5, 5, "枠の一言\n[9] C:\\x.png") }, "a.exe", "w", at, "[8] C:\\ocr.png\n読んだ文字"));
        var r = new Sender(new FakeClipboard(), d.Path, _ => { }).Send(q, at);
        Assert.Equal(SendOutcome.Sent, r.Outcome);
        return r.FallbackPath!;
    }

    [Fact]
    public void 送る文から件数と先頭の一言と画像の行を読む_字下げした行は拾わない()
    {
        using var d = new TempDir();
        var a = Png(d, "20261017", "090000-000.png");
        var b = Png(d, "20261017", "090001-000.png");
        var md = SendReal(d, new DateTime(2026, 10, 17, 9, 0, 5), (a, "最初の一言\n[7] C:\\comment.png"), (b, ""));
        var g = SendHistory.Load(d.Path).Single();
        Assert.True(g.Readable);
        Assert.Equal(md, g.MdPath);
        Assert.Equal(2, g.Count);
        Assert.Equal("最初の一言", g.FirstComment);
        Assert.Equal(new[] { a, b }, g.ImagePaths);   // 一言・枠・読み取った文字の続きの [n] は拾わない
        Assert.Equal(new DateTime(2026, 10, 17, 9, 0, 5), g.SentAt);
    }

    [Fact]
    public void 新しい順に並べ_日付のフォルダの_send_md_だけを見る()
    {
        using var d = new TempDir();
        var p1 = Png(d, "20261016", "1.png");
        var p2 = Png(d, "20261017", "2.png");
        var older = SendReal(d, new DateTime(2026, 10, 16, 23, 59, 0), (p1, "前日"));
        var newer = SendReal(d, new DateTime(2026, 10, 17, 8, 0, 0), (p2, "今日"));
        var newest = SendReal(d, new DateTime(2026, 10, 17, 8, 0, 0, 500), (p2, "同じ秒の後"));
        File.WriteAllText(Path.Combine(d.Path, "20261017", "memo.md"), "切り取り 1 件\n[1] " + p2);
        Directory.CreateDirectory(d.File("old"));
        File.WriteAllText(Path.Combine(d.Path, "old", "send-080000-000.md"), "切り取り 1 件\n[1] " + p2);
        File.WriteAllText(d.File("send-080000-000.md"), "切り取り 1 件\n[1] " + p2);   // 日付のフォルダの外
        Assert.Equal(new[] { newest, newer, older }, SendHistory.Load(d.Path).Select(g => g.MdPath));
    }

    [Fact]
    public void 保存先が無ければ空()
    {
        using var d = new TempDir();
        Assert.Empty(SendHistory.Load(d.File("none")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ただのメモ\n[1] C:\\a.png")]          // 1 行目が「切り取り n 件」でない
    [InlineData("切り取り 1 件（2026-10-17 09:00）\n\n一言: 画像の行が無い")]
    public void 送る文の形でなければ読めない行にする_E08(string text)
    {
        var g = SendHistory.Parse(@"C:\s\20261017\send-090000-000.md", DateTime.Now, text);
        Assert.False(g.Readable);
    }

    [Fact]
    public void 入れ直すと_mdと今在る画像を入れ_ためる一覧は変えない()
    {
        using var d = new TempDir();
        var a = Png(d, "20261017", "a.png");
        var b = Png(d, "20261017", "b.png");
        var md = SendReal(d, new DateTime(2026, 10, 17, 9, 0, 0), (a, "1"), (b, "2"));
        File.Delete(a);   // 7 日の自動削除などで消えた
        var q = new SnipQueue(d.File("queue.json"));
        var c = Png(d, "20261017", "c.png");
        q.Add(new SnipItem(c, "ためている", new List<SnipBox>(), "a.exe", "w", DateTime.Now));
        var cb = new FakeClipboard();
        var r = new Sender(cb, d.Path, _ => { }).Resend(SendHistory.Load(d.Path).Single());
        Assert.Equal(ResendOutcome.Copied, r.Outcome);
        Assert.Equal(1, r.ImageCount);
        Assert.Equal(new[] { md, b }, cb.Files);
        Assert.Single(q.Items);
        Assert.Single(new SnipQueue(d.File("queue.json")).Items);
    }

    [Fact]
    public void 保存先の外のファイルと_png_以外は入れない()
    {
        using var d = new TempDir();
        using var other = new TempDir();
        var inside = Png(d, "20261017", "in.png");
        var outside = Path.Combine(other.Path, "out.png"); File.WriteAllBytes(outside, new byte[] { 1 });
        var txt = Path.Combine(d.Path, "20261017", "x.txt"); File.WriteAllText(txt, "x");
        var sneaky = Path.Combine(d.Path, "20261017", "..", "..", Path.GetFileName(other.Path), "out.png");
        var md = Path.Combine(d.Path, "20261017", "send-090000-000.md");
        File.WriteAllText(md, $"切り取り 4 件（2026-10-17 09:00）\n\n[1] {inside}\n[2] {outside}\n[3] {txt}\n[4] {sneaky}\n");
        var g = SendHistory.Load(d.Path).Single();
        Assert.Equal(new[] { inside }, SendHistory.UsableImages(g, d.Path));
    }

    [Fact]
    public void 画像がすべて消えていればクリップボードに触れない_E08()
    {
        using var d = new TempDir();
        var a = Png(d, "20261017", "a.png");
        SendReal(d, new DateTime(2026, 10, 17, 9, 0, 0), (a, "1"));
        File.Delete(a);
        var cb = new FakeClipboard();
        var r = new Sender(cb, d.Path, _ => { }).Resend(SendHistory.Load(d.Path).Single());
        Assert.Equal(ResendOutcome.Unreadable, r.Outcome);
        Assert.Equal(0, cb.Calls);
    }

    [Fact]
    public void 送る文の_md_が開いている間に消えたら入れない_E08()
    {
        using var d = new TempDir();
        var a = Png(d, "20261017", "a.png");
        var md = SendReal(d, new DateTime(2026, 10, 17, 9, 0, 0), (a, "1"));
        var g = SendHistory.Load(d.Path).Single();
        File.Delete(md);
        var cb = new FakeClipboard();
        Assert.Equal(ResendOutcome.Unreadable, new Sender(cb, d.Path, _ => { }).Resend(g).Outcome);
        Assert.Equal(0, cb.Calls);
    }

    [Fact]
    public void クリップボードは3回まで0_2秒おきに試す_E08()
    {
        using var d = new TempDir();
        var a = Png(d, "20261017", "a.png");
        SendReal(d, new DateTime(2026, 10, 17, 9, 0, 0), (a, "1"));
        var g = SendHistory.Load(d.Path).Single();
        var ok = new FakeClipboard { FailTimes = 2 }; var waits = new List<int>();
        Assert.Equal(ResendOutcome.Copied, new Sender(ok, d.Path, waits.Add).Resend(g).Outcome);
        Assert.Equal(new[] { 200, 200 }, waits);
        var ng = new FakeClipboard { FailTimes = 3 };
        Assert.Equal(ResendOutcome.Failed, new Sender(ng, d.Path, _ => { }).Resend(g).Outcome);
        Assert.Equal(3, ng.Calls);
    }

    // verify V1: 取った日のフォルダが先に消えて画像が 1 枚も残らない送信は、履歴でも読めない行にする
    [Fact]
    public void 画像がすべて消えた送信は履歴で読めない行になる_E08()
    {
        using var d = new TempDir();
        var a = Png(d, "20261010", "a.png");   // 取ったのは 10/10
        SendReal(d, new DateTime(2026, 10, 17, 9, 0, 0), (a, "1"));   // 送ったのは 10/17
        Assert.True(SendHistory.Load(d.Path).Single().Readable);
        Directory.Delete(d.File("20261010"), true);   // R-15 で 10/10 のフォルダが消えた
        Assert.False(SendHistory.Load(d.Path).Single().Readable);
    }

    // verify V3: 件数が桁あふれしても履歴ごと落とさない
    [Fact]
    public void 件数が桁あふれしても読める()
    {
        var g = SendHistory.Parse(@"C:\s\20261017\send-090000-000.md", DateTime.Now, "切り取り 99999999999999999999 件（x）\n\n[1] C:\\s\\20261017\\a.png");
        Assert.Equal(1, g.Count);
        Assert.True(g.Readable);
    }

    // verify V4（M2・M3）: 保存先の大文字小文字は問わない。名前が保存先で始まるだけの隣のフォルダは外
    [Fact]
    public void 保存先は大文字小文字を問わず_隣のフォルダは外とみなす()
    {
        using var d = new TempDir();
        var inside = Png(d, "20261017", "in.png");
        var sibling = d.Path + "-x"; Directory.CreateDirectory(sibling);
        var outside = Path.Combine(sibling, "out.png"); File.WriteAllBytes(outside, new byte[] { 1 });
        try
        {
            var g = new SendGroup("x.md", DateTime.Now, 2, "", new[] { inside.ToUpperInvariant(), outside }, true);
            Assert.Single(SendHistory.UsableImages(g, d.Path.ToLowerInvariant()));
            Assert.Empty(SendHistory.UsableImages(g with { ImagePaths = new[] { outside } }, d.Path));
        }
        finally { Directory.Delete(sibling, true); }
    }

    // verify V2・V4（M21）: 読めない行は選べない。マウスで押したら元の行のまま、キーなら次の読める行へ
    [Fact]
    public void 読めない行は選べず_マウスでは隣の行に移らない()
    {
        SendGroup G(bool ok) => new("m.md", DateTime.Now, 1, "", new[] { "a.png" }, ok);
        var gs = new[] { G(true), G(false), G(true), G(false) };
        Assert.Same(gs[0], HistoryPick.Choose(gs, 0));
        Assert.Null(HistoryPick.Choose(gs, 1));
        Assert.Null(HistoryPick.Choose(gs, -1));
        Assert.Null(HistoryPick.Choose(gs, 9));
        Assert.Equal(0, HistoryPick.Adjust(gs, 1, 0, byMouse: true));    // 押した行（読めない）に移らず、元の行
        Assert.Equal(2, HistoryPick.Adjust(gs, 1, 0, byMouse: false));   // ↓ なら次の読める行
        Assert.Equal(0, HistoryPick.Adjust(gs, 1, 2, byMouse: false));   // ↑ なら前の読める行
        Assert.Equal(2, HistoryPick.Adjust(gs, 3, 2, byMouse: false));   // 先が無ければ元の行
        Assert.Equal(2, HistoryPick.Adjust(gs, 2, 0, byMouse: true));    // 読める行はそのまま
    }

    // verify V4（M20）: 知らせの文言（R-21）
    [Fact]
    public void 入れ直した後の知らせは要件の文言()
    {
        Assert.Equal("2 件をクリップボードに入れました。Ctrl＋V で貼れます", HistoryPick.Message(new ResendResult(ResendOutcome.Copied, 2)));
        Assert.Contains("画像が残っていない", HistoryPick.Message(new ResendResult(ResendOutcome.Unreadable, 0)));
        Assert.Contains("クリップボードに書けません", HistoryPick.Message(new ResendResult(ResendOutcome.Failed, 1)));
    }

    [Fact]
    public void 切り取り画面のHは画面を閉じて履歴を出す_ほかの状態では何もしない()
    {
        var m = new SnipStateMachine(SnipState.Selecting);
        Assert.Equal(new[] { SnipAction.Close, SnipAction.ShowHistory }, m.Handle(SnipEvent.History));
        Assert.Equal(SnipState.Idle, m.State);
        foreach (var s in new[] { SnipState.Idle, SnipState.Delaying, SnipState.Editing })
        {
            var x = new SnipStateMachine(s);
            Assert.Empty(x.Handle(SnipEvent.History));
            Assert.Equal(s, x.State);
        }
    }

    // verify M3: 実際の自動削除（Cleanup.Run）の後に履歴を読む。.md ごと消えた送信は出ない。
    // .md は残り画像だけ消えた送信（取った日と送った日が違う）は「（読めません）」
    [Fact]
    public void M3_自動削除の後の履歴()
    {
        using var d = new TempDir();
        var gone = Png(d, "20261001", "090000-000.png");
        SendReal(d, new DateTime(2026, 10, 1, 9, 0, 5), (gone, "md ごと消える"));
        var oldImg = Png(d, "20261009", "235900-000.png");
        var crossDay = SendReal(d, new DateTime(2026, 10, 11, 0, 0, 5), (oldImg, "画像だけ消える"));
        var kept = Png(d, "20261017", "090000-000.png");
        var fresh = SendReal(d, new DateTime(2026, 10, 17, 9, 0, 5), (kept, "残る"));
        Assert.Equal(3, SendHistory.Load(d.Path).Count);

        Cleanup.Run(d.Path, new DateTime(2026, 10, 18), 7, Array.Empty<string>());

        var after = SendHistory.Load(d.Path);
        Assert.Equal(new[] { fresh, crossDay }, after.Select(g => g.MdPath));
        Assert.True(after[0].Readable);
        Assert.False(after[1].Readable);
        Assert.Empty(after[1].Present);
        Assert.Null(HistoryPick.Choose(after, 1));
    }

    // verify M2: 一部の画像だけ消えた送信は、入れられる枚数を出し（書いた件数も添える）、縮小はいま在る最初の画像から作る
    [Fact]
    public void M2_一部の画像が消えた送信は_入れられる枚数と在る画像を返す()
    {
        using var d = new TempDir();
        var oldImg = Png(d, "20261009", "235900-000.png");
        var newImg = Png(d, "20261011", "000000-000.png");
        SendReal(d, new DateTime(2026, 10, 11, 0, 0, 5), (oldImg, "前日に取った"), (newImg, "当日に取った"));
        Cleanup.Run(d.Path, new DateTime(2026, 10, 18), 7, Array.Empty<string>());
        var g = SendHistory.Load(d.Path).Single();
        Assert.True(g.Readable);
        Assert.Equal(2, g.Count);
        Assert.Equal(new[] { newImg }, g.Present);   // 縮小は Present[0]（消えた 1 枚目ではなく）から
        Assert.Equal("10/11 00:00　1 件（送ったときは 2 件）", HistoryPick.Head(g));
        var clip = new FakeClipboard();
        var r = new Sender(clip, d.Path, _ => { }).Resend(g);
        Assert.Equal(1, r.ImageCount);
        Assert.Equal(new[] { g.MdPath, newImg }, clip.Files);
    }

    [Fact]
    public void M2_見出し_全部ある送信と読めない送信()
    {
        var at = new DateTime(2026, 10, 17, 9, 5, 0);
        var all = new SendGroup("x.md", at, 2, "", new[] { "a", "b" }, true) { Present = new[] { "a", "b" } };
        Assert.Equal("10/17 09:05　2 件", HistoryPick.Head(all));
        Assert.Equal("10/17 09:05　2 件　（読めません）", HistoryPick.Head(all with { Readable = false, Present = Array.Empty<string>() }));
    }
}

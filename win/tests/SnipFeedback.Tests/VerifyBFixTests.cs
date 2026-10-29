using SnipFeedback.Core;

namespace SnipFeedback.Tests;

// 再 verify（docs/verify/20261011_snip-v2_verify-b.md）の指摘への対応
public class VerifyBFixTests
{
    private static SnipItem Item(string path) => new(path, "一言", new List<SnipBox>(), "a.exe", "w", new DateTime(2026, 10, 10, 9, 0, 0));

    // queue.json と同じ名前のフォルダを置くと、それ以降の書き込みが失敗する
    private static void BreakWrites(TempDir d) { File.Delete(d.File("queue.json")); Directory.CreateDirectory(d.File("queue.json")); }

    private sealed class OkClipboard : IClipboardWriter { public List<string>? Files; public bool TrySetFiles(IReadOnlyList<string> p) { Files = p.ToList(); return true; } }

    [Fact]
    public void V12_クリップボードに書けたが一覧を空にできないときは別の結果で返し_一覧は残す()
    {
        using var d = new TempDir();
        var a = d.File("a.png"); File.WriteAllBytes(a, new byte[] { 1 });
        var q = new SnipQueue(d.File("queue.json"));
        q.Add(Item(a));
        BreakWrites(d);
        var cb = new OkClipboard();
        var r = new Sender(cb, d.Path, _ => { }).Send(q, DateTime.Now);
        Assert.Equal(SendOutcome.SentButNotCleared, r.Outcome);
        Assert.NotNull(cb.Files);
        Assert.Single(q.Items);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(60, 60)]
    [InlineData(61, 60)]
    [InlineData(int.MaxValue, 60)]
    public void V13_遅延の秒数は1から60に丸める(int given, int expected)
    {
        using var d = new TempDir();
        File.WriteAllText(d.File("config.json"), $$"""{ "delaySeconds": {{given}} }""");
        Assert.Equal(expected, Settings.Load(d.File("config.json"), out _).DelaySeconds);
    }

    [Fact]
    public void V14_補正した値は知らせ_書き戻してよいかを返す()
    {
        using var d = new TempDir();
        File.WriteAllText(d.File("config.json"), """{ "keepDays": 0 }""");
        var s = Settings.Load(d.File("config.json"), out var error);
        Assert.NotNull(error);                 // 補正したことを知らせる
        Assert.False(s.CanSave);               // 利用者の値を上書きしない
        File.WriteAllText(d.File("ok.json"), """{ "keepDays": 3 }""");
        var ok = Settings.Load(d.File("ok.json"), out var e2);
        Assert.Null(e2);
        Assert.True(ok.CanSave);
        File.WriteAllText(d.File("broken.json"), "{壊れ");
        Assert.False(Settings.Load(d.File("broken.json"), out _).CanSave);
    }

    [Fact]
    public void V15_設定と一覧のファイルを開けなくても既定値と空の一覧で起動できる()
    {
        using var d = new TempDir();
        File.WriteAllText(d.File("config.json"), "{}");
        File.WriteAllText(d.File("queue.json"), "[]");
        using (new FileStream(d.File("config.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        using (new FileStream(d.File("queue.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var s = Settings.Load(d.File("config.json"), out var error);
            Assert.NotNull(error);
            Assert.Equal("Ctrl+Alt+S", s.Hotkey);
            var q = new SnipQueue(d.File("queue.json"));
            Assert.Empty(q.Items);
            Assert.NotNull(q.LoadError);
        }
    }

    [Fact]
    public void V16_一件消すときに書けなければ一覧も画像も元のまま()
    {
        using var d = new TempDir();
        var a = d.File("a.png"); File.WriteAllBytes(a, new byte[] { 1 });
        var q = new SnipQueue(d.File("queue.json"));
        q.Add(Item(a));
        BreakWrites(d);
        Assert.ThrowsAny<IOException>(() => q.Remove(0));
        Assert.Single(q.Items);
        Assert.True(File.Exists(a));
    }

    [Fact]
    public void V17_壊れた一覧を退避した日から数える()
    {
        using var d = new TempDir();
        File.WriteAllText(d.File("queue.json"), "{壊れ");
        File.SetLastWriteTime(d.File("queue.json"), DateTime.Now.AddDays(-30));
        _ = new SnipQueue(d.File("queue.json"));
        Cleanup.Run(d.Path, DateTime.Today, 7, Array.Empty<string>());
        Assert.Single(Directory.GetFiles(d.Path, "queue.json.broken-*"));
    }

    [Fact]
    public void V18_null_の項目と_null_の枠は飛ばして読む()
    {
        using var d = new TempDir();
        var png = d.File("a.png"); File.WriteAllBytes(png, new byte[] { 1 });
        File.WriteAllText(d.File("queue.json"), "[null,{\"ImagePath\":" + System.Text.Json.JsonSerializer.Serialize(png) + ",\"Comment\":\"c\",\"Boxes\":[null,{\"Number\":1,\"X\":0,\"Y\":0,\"Width\":9,\"Height\":9,\"Comment\":null}],\"AppName\":\"a\",\"WindowTitle\":\"w\",\"TakenAt\":\"2026-10-10T09:00:00\"}]");
        var q = new SnipQueue(d.File("queue.json"));
        Assert.Single(q.Items);
        Assert.Single(q.Items[0].Boxes);
        Assert.Equal("", q.Items[0].Boxes[0].Comment);
    }

    [Fact]
    public void V18_設定は小文字始まりのキー名で書く_READMEのとおり()
    {
        using var d = new TempDir();
        new Settings().Save(d.File("config.json"));
        var text = File.ReadAllText(d.File("config.json"));
        foreach (var k in new[] { "\"hotkey\"", "\"delaySeconds\"", "\"saveDir\"", "\"keepDays\"", "\"lastMode\"" }) Assert.Contains(k, text);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(0, 1)]
    [InlineData(7, 7)]
    public void V18_保存日数の境界(int given, int expected)
    {
        using var d = new TempDir();
        File.WriteAllText(d.File("config.json"), $$"""{ "keepDays": {{given}} }""");
        Assert.Equal(expected, Settings.Load(d.File("config.json"), out _).KeepDays);
    }
}

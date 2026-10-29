using SnipFeedback.Core;

namespace SnipFeedback.Tests;

// R-15（取った日から keepDays を過ぎた画像を消す。一覧にあるものは消さない。AC-9）
public class CleanupTests
{
    [Fact]
    public void 八日前は消し_七日前は残し_一覧にあるものは消さない_空のフォルダも消す()
    {
        using var d = new TempDir();
        var today = new DateTime(2026, 10, 18);
        string Put(string day, string name) { Directory.CreateDirectory(d.File(day)); var p = d.File(day, name); File.WriteAllBytes(p, new byte[] { 1 }); return p; }
        var old1 = Put("20261010", "090000-000.png");        // 8 日前 → 消す
        var oldMd = Put("20261010", "send-090000.md");        // 8 日前 → 消す
        var keep = Put("20261009", "090000-000.png");         // 9 日前だが一覧にある → 残す
        var week = Put("20261011", "090000-000.png");         // 7 日前 → 残す
        var other = Put("20261010", "memo.txt");              // 本ツールのものでない → 残す
        Put("20261001", "080000-000.png");                    // 17 日前 → 消して空のフォルダも消す
        File.WriteAllText(d.File("queue.json"), "[]");

        var removed = Cleanup.Run(d.Path, today, 7, new[] { keep });

        Assert.False(File.Exists(old1));
        Assert.False(File.Exists(oldMd));
        Assert.True(File.Exists(keep));
        Assert.True(File.Exists(week));
        Assert.True(File.Exists(other));
        Assert.False(Directory.Exists(d.File("20261001")));
        Assert.True(File.Exists(d.File("queue.json")));
        Assert.Equal(3, removed);
    }

    [Fact]
    public void 保存先が無ければ何もしない()
    {
        Assert.Equal(0, Cleanup.Run(@"C:\no\such\dir-snipfb", DateTime.Now, 7, Array.Empty<string>()));
    }
}

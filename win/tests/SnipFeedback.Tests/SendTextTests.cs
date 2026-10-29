using SnipFeedback.Core;

namespace SnipFeedback.Tests;

// R-13 の送る文
public class SendTextTests
{
    private static SnipItem Item(string path, string comment, params SnipBox[] boxes) =>
        new(path, comment, boxes.ToList(), "WindowsTerminal.exe", "PowerShell", new DateTime(2026, 10, 10, 14, 28, 12));

    [Fact]
    public void 件数と時刻_番号_パス_一言_アプリ名_ウィンドウ名_時刻_枠ごとの番号と一言_末尾の1行()
    {
        var items = new[]
        {
            Item(@"C:\s\20261010\142812-104.png", "このエラーの原因は？", new SnipBox(1, 10, 20, 30, 40, "ここの行"), new SnipBox(2, 0, 0, 10, 10, "")),
            Item(@"C:\s\20261010\142900-001.png", "2行目\n続き"),
        };
        var t = SendText.Build(items, new DateTime(2026, 10, 10, 14, 30, 0));
        Assert.Equal(string.Join("\n", new[]
        {
            "切り取り 2 件（2026-10-10 14:30）",
            "",
            @"[1] C:\s\20261010\142812-104.png",
            "一言: このエラーの原因は？",
            "アプリ: WindowsTerminal.exe ／ ウィンドウ: PowerShell ／ 時刻: 14:28:12",
            "  枠1: ここの行",
            "  枠2: （一言なし）",
            "",
            @"[2] C:\s\20261010\142900-001.png",
            "一言: 2行目",
            "  続き",
            "アプリ: WindowsTerminal.exe ／ ウィンドウ: PowerShell ／ 時刻: 14:28:12",
            "",
            "対象ごとに番号で回答し、迷う点は質問として返してください。",
        }), t);
    }

    [Fact]
    public void 一言が空なら一言なしと書く()
    {
        var t = SendText.Build(new[] { Item(@"C:\a.png", "  ") }, DateTime.Now);
        Assert.Contains("一言: （一言なし）", t);
    }
}

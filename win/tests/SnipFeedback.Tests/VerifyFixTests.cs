using System.Drawing;
using SnipFeedback.Core;

namespace SnipFeedback.Tests;

// verify（docs/verify/20261011_snip-v2_verify.md）の指摘への対応
public class VerifyFixTests
{
    [Theory]
    [InlineData(SnipState.Selecting)]
    [InlineData(SnipState.Editing)]
    [InlineData(SnipState.Delaying)]
    public void V1_V2_画面が想定外に閉じたり例外が起きたら待機に戻り_次の起動キーが効く(SnipState from)
    {
        var m = new SnipStateMachine(from);
        m.Reset();
        Assert.Equal(SnipState.Idle, m.State);
        Assert.Equal(new[] { SnipAction.GrabAndShowSelector }, m.Handle(SnipEvent.Hotkey));
    }

    [Fact]
    public void V4_設定の値の範囲を正す()
    {
        using var d = new TempDir();
        File.WriteAllText(d.File("config.json"), """{ "DelaySeconds": 0, "KeepDays": -3, "SaveDir": "rel\\dir", "Hotkey": "" }""");
        var s = Settings.Load(d.File("config.json"), out var error);
        Assert.NotNull(error);   // verify-b V14: 正したことを知らせる
        Assert.Equal(1, s.DelaySeconds);
        Assert.Equal(1, s.KeepDays);
        Assert.Equal(PublicReleaseTests.DefaultSaveDir, s.SaveDir);   // r14: 既定は %LOCALAPPDATA%\SnipFeedback\snips
        Assert.Equal("Ctrl+Alt+S", s.Hotkey);
    }

    [Fact]
    public void V4_キー名は小文字でも読む_READMEのとおり()
    {
        using var d = new TempDir();
        File.WriteAllText(d.File("config.json"), """{ "hotkey": "Ctrl+Shift+F9", "delaySeconds": 5, "keepDays": 10, "lastMode": "Window" }""");
        var s = Settings.Load(d.File("config.json"), out var error);
        Assert.Null(error);
        Assert.Equal("Ctrl+Shift+F9", s.Hotkey);
        Assert.Equal(5, s.DelaySeconds);
        Assert.Equal(10, s.KeepDays);
        Assert.Equal(SnipMode.Window, s.LastMode);
    }

    [Fact]
    public void V5_queue_json_の欠けた項目は空として読む()
    {
        using var d = new TempDir();
        var png = d.File("a.png"); File.WriteAllBytes(png, new byte[] { 1 });
        File.WriteAllText(d.File("queue.json"), "[{\"ImagePath\":" + System.Text.Json.JsonSerializer.Serialize(png) + ",\"Comment\":null,\"Boxes\":null,\"AppName\":null,\"WindowTitle\":null,\"TakenAt\":\"2026-10-10T09:00:00\"}]");
        var q = new SnipQueue(d.File("queue.json"));
        Assert.Single(q.Items);
        Assert.Empty(q.Items[0].Boxes);
        var text = SendText.Build(q.Items, DateTime.Now);   // 落ちない
        Assert.Contains("一言: （一言なし）", text);
        Assert.Contains("アプリ: 不明 ／ ウィンドウ: 不明", text);
    }

    [Fact]
    public void V9_壊れた一覧の退避ファイルも期限を過ぎたら消す()
    {
        using var d = new TempDir();
        var old = d.File("queue.json.broken-20261001090000");
        var fresh = d.File("queue.json.broken-20261017090000");
        File.WriteAllText(old, "x"); File.WriteAllText(fresh, "x");
        File.SetLastWriteTime(old, new DateTime(2026, 10, 1, 9, 0, 0));
        File.SetLastWriteTime(fresh, new DateTime(2026, 10, 17, 9, 0, 0));
        Cleanup.Run(d.Path, new DateTime(2026, 10, 18), 7, Array.Empty<string>());
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void V7_R10_ウィンドウの切り取り方ならクリックしたウィンドウ_四角なら範囲の中心のウィンドウ_r4()
    {
        var code = new WindowInfo((IntPtr)1, new Rectangle(0, 0, 500, 500), "code.exe", "VS Code");
        var term = new WindowInfo((IntPtr)2, new Rectangle(600, 0, 500, 500), "WindowsTerminal.exe", "PowerShell");
        var all = new[] { code, term };
        // ウィンドウの切り取り方: クリックしたもの
        Assert.Equal(("code.exe", "VS Code"), SnipSource.Choose(code, all, new Rectangle(0, 0, 500, 500), "claude.exe", "Claude"));
        // 四角（r4）: 範囲の中心にあるウィンドウ。範囲の中心がターミナルの上
        Assert.Equal(("WindowsTerminal.exe", "PowerShell"), SnipSource.Choose(null, all, new Rectangle(400, 100, 500, 100), "explorer.exe", "Program Manager"));
        // 中心に何も無ければ前面のウィンドウ
        Assert.Equal(("explorer.exe", "Program Manager"), SnipSource.Choose(null, all, new Rectangle(1200, 600, 50, 50), "explorer.exe", "Program Manager"));
    }

    [Fact]
    public void V7_R15_日付が変わったときだけ知らせる()
    {
        var w = new DayWatcher(new DateTime(2026, 10, 11, 23, 58, 0));
        Assert.False(w.Changed(new DateTime(2026, 10, 11, 23, 59, 0)));
        Assert.True(w.Changed(new DateTime(2026, 10, 12, 0, 0, 30)));
        Assert.False(w.Changed(new DateTime(2026, 10, 12, 0, 1, 30)));
    }
}

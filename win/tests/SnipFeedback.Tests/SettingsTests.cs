using SnipFeedback.Core;

namespace SnipFeedback.Tests;

// N-04（設定ファイル）
public class SettingsTests
{
    [Fact]
    public void 無ければ既定値で作る()
    {
        using var d = new TempDir();
        var s = Settings.Load(d.File("cfg", "config.json"), out var error);
        Assert.Null(error);
        Assert.Equal("Ctrl+Alt+S", s.Hotkey);
        Assert.Equal(3, s.DelaySeconds);
        Assert.Equal(7, s.KeepDays);
        Assert.Equal(PublicReleaseTests.DefaultSaveDir, s.SaveDir);   // r14: 既定は %LOCALAPPDATA%\SnipFeedback\snips
        Assert.Equal(SnipMode.Rect, s.LastMode);
        Assert.True(File.Exists(d.File("cfg", "config.json")));
    }

    [Fact]
    public void 書いた値を読み戻せる()
    {
        using var d = new TempDir();
        var p = d.File("config.json");
        var s = Settings.Load(p, out _);
        s.Hotkey = "Ctrl+Shift+F9"; s.DelaySeconds = 5; s.LastMode = SnipMode.Window;
        s.Save(p);
        var s2 = Settings.Load(p, out var error);
        Assert.Null(error);
        Assert.Equal("Ctrl+Shift+F9", s2.Hotkey);
        Assert.Equal(5, s2.DelaySeconds);
        Assert.Equal(SnipMode.Window, s2.LastMode);
    }

    [Fact]
    public void 壊れていれば既定値で動き_理由を返す()
    {
        using var d = new TempDir();
        File.WriteAllText(d.File("config.json"), "{壊れ");
        var s = Settings.Load(d.File("config.json"), out var error);
        Assert.NotNull(error);
        Assert.Equal("Ctrl+Alt+S", s.Hotkey);
    }

    [Theory]
    [InlineData("Ctrl+Alt+S", true, 0x2 | 0x1, 0x53)]
    [InlineData("ctrl + shift + f9", true, 0x2 | 0x4, 0x78)]
    [InlineData("Win+Alt+1", true, 0x8 | 0x1, 0x31)]
    [InlineData("S", false, 0, 0)]               // 修飾キーが無い
    [InlineData("Ctrl+Alt+Nope", false, 0, 0)]
    public void 起動キーの文字列を読む(string text, bool ok, int mods, int vk)
    {
        Assert.Equal(ok, HotkeySpec.TryParse(text, out var m, out var k));
        if (ok) { Assert.Equal((uint)mods, m); Assert.Equal(vk, k); }
    }
}

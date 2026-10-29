using Microsoft.Win32;
using SnipFeedback.Core;

namespace SnipFeedback.Tests;

// H-M35 1a（要件 r11）: トレイの「終了」は次のサインインまで止める。
// 本物は HKCU\Volatile Environment（サインアウトで消える）に印を置く。テストは使い捨てのキーで確かめる
public class PauseFlagTests
{
    [Fact]
    public void 印を置くと止まっていると分かり_消すと止まっていない()
    {
        var key = @"Software\SnipFeedbackTests\" + Guid.NewGuid().ToString("N");
        try
        {
            var f = new PauseFlag(key);
            Assert.False(f.IsSet);
            f.Set();
            Assert.True(new PauseFlag(key).IsSet);
            f.Clear();
            Assert.False(new PauseFlag(key).IsSet);
            f.Clear();   // 無くても落ちない
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(@"Software\SnipFeedbackTests", false); }
    }

    [Fact]
    public void 本物の置き場所はサインアウトで消えるキー()
    {
        Assert.Equal("Volatile Environment", PauseFlag.SessionKey);
    }
}

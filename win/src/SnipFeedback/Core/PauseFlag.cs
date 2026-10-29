using Microsoft.Win32;

namespace SnipFeedback.Core;

// H-M35 1a（要件 r11）: トレイの「終了」で次のサインインまで止めるための印。
// HKCU\Volatile Environment はサインインごとに作り直され、サインアウト・再起動で消える。
// 印があれば、タスク スケジューラの合図（5 分ごと・ロック解除・スリープ明け）で起動されても、すぐ終わる
public sealed class PauseFlag
{
    public const string SessionKey = "Volatile Environment";
    private const string ValueName = "SnipFeedbackPaused";
    private readonly string _key;

    public PauseFlag(string key = SessionKey) => _key = key;

    public bool IsSet
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(_key);
            return k?.GetValue(ValueName) != null;
        }
    }

    public void Set()
    {
        // キーが無ければ揮発性（サインアウトで消える）で作る（verify-b K12）
        using var k = Registry.CurrentUser.CreateSubKey(_key, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryOptions.Volatile);
        k.SetValue(ValueName, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    public void Clear()
    {
        using var k = Registry.CurrentUser.OpenSubKey(_key, writable: true);
        k?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

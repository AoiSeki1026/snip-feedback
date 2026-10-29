using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnipFeedback.Core;

// 設定ファイル（N-04）。%APPDATA%\SnipFeedback\config.json
public sealed class Settings
{
    public string Hotkey { get; set; } = "Ctrl+Alt+S";
    public int DelaySeconds { get; set; } = 3;
    public string SaveDir { get; set; } = DefaultSaveDir;

    // 既定の保存先（R-11、r14）。利用者ごとの、同期されない場所。本人は config.json の saveDir で今の場所を指定している
    public static string DefaultSaveDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnipFeedback", "snips");
    public int KeepDays { get; set; } = 7;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SnipMode LastMode { get; set; } = SnipMode.Rect;   // R-02: 前回使った切り取り方
    public int? BadgeX { get; set; }                          // R-20: 浮かせた表示を動かした位置（未設定なら右下）
    public int? BadgeY { get; set; }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnipFeedback", "config.json");

    // キー名は小文字始まりで書き、大文字小文字を問わず読む（README の案内どおり。verify V4）
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // 書き戻してよいか。読めなかった・値を正したときは false にして、利用者の書いた値を上書きしない（verify-b V14）
    [JsonIgnore]
    public bool CanSave { get; private set; } = true;

    // 無ければ既定値で作る。読めない・値を正したときは既定値／正した値で動き、理由を error に返す（ファイルは書き換えない）
    public static Settings Load(string path, out string? error)
    {
        error = null;
        if (!File.Exists(path))
        {
            var s = new Settings();
            try { s.Save(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { s.CanSave = false; error = $"設定ファイルを作れません（{ex.Message}）。既定値で動きます: {path}"; }
            return s;
        }
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json) ?? new Settings();
            var fixes = s.Normalize();
            if (fixes.Count > 0)
            {
                s.CanSave = false;
                error = $"設定ファイルの値を正して動きます（{string.Join("、", fixes)}）。ファイルは書き換えていません: {path}";
            }
            return s;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)   // V15: 開けなくても起動する
        {
            error = $"設定ファイルを読めません（{ex.Message}）。既定値で動きます: {path}";
            return new Settings { CanSave = false };
        }
    }

    // 値の範囲を正す（verify V4・V13）。0 日だと送った画像がすぐ消え、相対パスだと送る文に使えないパスが入る。
    // 遅延は 60 秒まで（大きすぎるとタイマーの値があふれて D が毎回失敗する）
    public const int MaxDelaySeconds = 60;
    private List<string> Normalize()
    {
        var d = new Settings();
        var fixes = new List<string>();
        if (string.IsNullOrWhiteSpace(Hotkey)) { Hotkey = d.Hotkey; fixes.Add($"hotkey → {Hotkey}"); }
        if (DelaySeconds < 1 || DelaySeconds > MaxDelaySeconds) { DelaySeconds = Math.Clamp(DelaySeconds, 1, MaxDelaySeconds); fixes.Add($"delaySeconds → {DelaySeconds}"); }
        if (KeepDays < 1) { KeepDays = 1; fixes.Add("keepDays → 1"); }
        // ドライブ名で始まる場所（C:\…）だけを使う。UNC（\\server・//server）や \\?\・\\.\・\??\ の形は既定に正す。
        // 画像・一言を手元の外（ネットワークの共有など）へ書かないように（R-23、r14。verify H-M42〜44 の 1、verify-b の 1）
        if (string.IsNullOrWhiteSpace(SaveDir) || !IsLocalDrivePath(SaveDir)) { SaveDir = d.SaveDir; fixes.Add($"saveDir → {SaveDir}"); }
        return fixes;
    }

    // 許可リストで判定する（正規化した結果が「英字・:・\」で始まるものだけ）。正規化できないものも使わない
    private static bool IsLocalDrivePath(string path)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path)) return false;
            var full = Path.GetFullPath(path);
            return full.Length >= 3 && char.IsAsciiLetter(full[0]) && full[1] == ':' && full[2] == '\\';
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }
}

// 起動キーの文字列（例 "Ctrl+Alt+S"）を RegisterHotKey の修飾と仮想キーに変える
public static class HotkeySpec
{
    public const uint Alt = 0x1, Ctrl = 0x2, Shift = 0x4, Win = 0x8;

    public static bool TryParse(string text, out uint modifiers, out int vk)
    {
        modifiers = 0; vk = 0;
        var parts = text.Split('+').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        if (parts.Count < 2) return false;   // 修飾キーが要る
        foreach (var p in parts.Take(parts.Count - 1))
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= Ctrl; break;
                case "alt": modifiers |= Alt; break;
                case "shift": modifiers |= Shift; break;
                case "win": modifiers |= Win; break;
                default: modifiers = 0; return false;
            }
        }
        var key = parts[^1].ToUpperInvariant();
        if (key.Length == 1 && (char.IsAsciiLetterUpper(key[0]) || char.IsAsciiDigit(key[0]))) vk = key[0];
        else if (key.Length is 2 or 3 && key[0] == 'F' && int.TryParse(key[1..], out var n) && n is >= 1 and <= 24) vk = 0x70 + n - 1;
        else { modifiers = 0; return false; }
        return true;
    }
}

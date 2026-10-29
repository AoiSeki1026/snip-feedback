using System.Globalization;
using System.Text.RegularExpressions;

namespace SnipFeedback.Core;

// 取った日から keepDays を過ぎた画像と送信の控えを消す（R-15）。一覧にあるものは消さない
public static class Cleanup
{
    private static readonly Regex DayDir = new(@"^\d{8}$");
    // 本ツールが書くファイルだけを対象にする（HHmmss-fff.png と send-HHmmss-fff.md。古い send-HHmmss.md も）
    private static readonly Regex OwnFile = new(@"^(\d{6}-\d{3}\.png|send-\d{6}(-\d{3})?\.md)$");   // send-HHmmss-fff.md（verify S11）

    // 控え（queue.json.unread-*・broken-*）に載っている画像のパス。控えがある間はこれらの画像も消さない（T-09b、verify-d V28）。
    // 壊れた控えも読める範囲から拾う（"ImagePath":"…" を探す）
    private static readonly Regex ImagePathJson = new("\"ImagePath\"\\s*:\\s*(\"(?:[^\"\\\\]|\\\\.)*\")");
    // complete: 期限内の控えをすべて読めたか。読めない控えがあれば false（その控えにだけ載る画像を消さないよう、呼び出し側は自動削除を止める。verify T2）。
    // 期限（書いた日から keepDays）を過ぎた控えは、読めなくても止める理由にしない（Run がその回に消すので、止まり続けて溜まらない。verify-b U1）
    public static List<string> BackupReferences(string saveDir, DateTime today, int keepDays, out bool complete)
    {
        complete = true;
        var found = new List<string>();
        if (!Directory.Exists(saveDir)) return found;
        foreach (var f in Directory.GetFiles(saveDir, "queue.json.unread-*").Concat(Directory.GetFiles(saveDir, "queue.json.broken-*")))
        {
            string text;
            try { text = File.ReadAllText(f); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (!Expired(f, today, keepDays)) complete = false;
                continue;
            }
            foreach (Match m in ImagePathJson.Matches(text))
            {
                try { if (System.Text.Json.JsonSerializer.Deserialize<string>(m.Groups[1].Value) is string p && TryFull(p) != null) found.Add(p); }   // 使えるパスだけ（verify T1）
                catch (System.Text.Json.JsonException) { }
            }
        }
        return found;
    }

    private static bool Expired(string file, DateTime today, int keepDays)
    {
        try { return (today.Date - File.GetLastWriteTime(file).Date).Days > keepDays; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static string? TryFull(string? p)
    {
        if (string.IsNullOrWhiteSpace(p) || p.Length > 32767 || p.Contains('\0')) return null;
        try { return Path.IsPathFullyQualified(p) ? Path.GetFullPath(p) : null; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    public static int Run(string saveDir, DateTime today, int keepDays, IEnumerable<string> keep)
    {
        if (!Directory.Exists(saveDir)) return 0;
        // おかしなパス（空白だけ・NUL・長すぎる・相対）は無視する。例外で自動削除ごと落ちないように（verify T1）
        var keepSet = new HashSet<string>(keep.Select(TryFull).OfType<string>(), StringComparer.OrdinalIgnoreCase);
        int removed = 0;
        // 壊れた一覧の退避ファイル（一言・ウィンドウ名を含む）も、書いた日から keepDays を過ぎたら消す（verify V9）
        foreach (var f in Directory.GetFiles(saveDir, "queue.json.broken-*").Concat(Directory.GetFiles(saveDir, "queue.json.unread-*")))
        {
            if ((today.Date - File.GetLastWriteTime(f).Date).Days <= keepDays) continue;
            try { File.Delete(f); removed++; } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        foreach (var dir in Directory.GetDirectories(saveDir))
        {
            var name = Path.GetFileName(dir);
            if (!DayDir.IsMatch(name) ||
                !DateTime.TryParseExact(name, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) continue;
            if ((today.Date - day).Days <= keepDays) continue;
            if (SnipFiles.IsReparsePoint(dir)) continue;   // ジャンクションの先は消さない（R-23）
            foreach (var f in Directory.GetFiles(dir))
            {
                if (!OwnFile.IsMatch(Path.GetFileName(f)) || keepSet.Contains(Path.GetFullPath(f)) || SnipFiles.IsReparsePoint(f)) continue;
                try { File.Delete(f); removed++; } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch (IOException) { }
        }
        return removed;
    }
}

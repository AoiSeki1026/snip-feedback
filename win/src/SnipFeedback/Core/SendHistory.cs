using System.Globalization;
using System.Text.RegularExpressions;

namespace SnipFeedback.Core;

// 1 回の送信（R-21）。Readable が false の行は履歴で選べない（E-08）
public sealed record SendGroup(string MdPath, DateTime SentAt, int Count, string FirstComment, IReadOnlyList<string> ImagePaths, bool Readable)
{
    // 読んだ時点で入れ直せる画像（UsableImages）。Load が埋める。一覧の件数と縮小に使う（verify M2）
    public IReadOnlyList<string> Present { get; init; } = Array.Empty<string>();
}

// 送った単位の履歴（R-21、r9）。保存先に残っている send-HHmmss-fff.md（R-13 の送る文、R-15 で 7 日。自動削除が止まっている間はそれより古いものも残る）を読む。新しく保存するものは無い
public static class SendHistory
{
    private static readonly Regex DayDir = new(@"^\d{8}$");
    private static readonly Regex MdName = new(@"^send-(\d{6}-\d{3})\.md$", RegexOptions.IgnoreCase);
    private static readonly Regex Header = new(@"^切り取り (\d+) 件");
    private static readonly Regex ImageLine = new(@"^\[(\d+)\] (.+)$");   // 行頭（字下げなし）だけ。一言などの続きの行は字下げされている

    // 新しい順。日付のフォルダ（YYYYMMDD）の send-*.md だけを見る
    public static List<SendGroup> Load(string saveDir)
    {
        var list = new List<SendGroup>();
        if (!Directory.Exists(saveDir)) return list;
        IEnumerable<string> days;
        try { days = Directory.EnumerateDirectories(saveDir).Where(d => DayDir.IsMatch(Path.GetFileName(d)) && !SnipFiles.IsReparsePoint(d)).ToList(); }   // ジャンクションの先は読まない（R-23）
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return list; }
        foreach (var day in days)
        {
            List<string> files;
            try { files = Directory.EnumerateFiles(day, "send-*.md").Where(f => MdName.IsMatch(Path.GetFileName(f)) && !SnipFiles.IsReparsePoint(f)).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var md in files)
            {
                var g = Read(md, Path.GetFileName(day));
                // 画像がすべて消えた送信も読めない行にする（E-08。R-15 は日付のフォルダごとに消すので、取った日と送った日が違うと起きる。verify V1）
                var present = g.Readable ? UsableImages(g, saveDir) : new List<string>();
                list.Add(g with { Present = present, Readable = g.Readable && present.Count > 0 });
            }
        }
        return list.OrderByDescending(g => g.SentAt).ThenByDescending(g => g.MdPath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static SendGroup Read(string md, string day)
    {
        var at = SentAtOf(md, day);
        try { return Parse(md, at, File.ReadAllText(md)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new SendGroup(md, at, 0, "", Array.Empty<string>(), false); }
    }

    // 時刻はファイル名（日付のフォルダ＋HHmmss-fff）から。読めなければ更新時刻
    private static DateTime SentAtOf(string md, string day)
    {
        var m = MdName.Match(Path.GetFileName(md));
        if (m.Success && DateTime.TryParseExact(day + m.Groups[1].Value, "yyyyMMddHHmmss-fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) return at;
        try { return File.GetLastWriteTime(md); } catch { return DateTime.MinValue; }
    }

    public static SendGroup Parse(string md, DateTime sentAt, string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var h = lines.Length > 0 ? Header.Match(lines[0]) : Match.Empty;
        var images = new List<string>();
        string? first = null;
        foreach (var l in lines.Skip(1))
        {
            var m = ImageLine.Match(l);
            if (m.Success) { images.Add(m.Groups[2].Value.Trim()); continue; }
            if (first == null && l.StartsWith("一言: ")) first = l["一言: ".Length..].Trim();
        }
        var readable = h.Success && images.Count > 0;
        // 桁あふれでも履歴ごと落とさない（verify V3）
        var count = h.Success && int.TryParse(h.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : images.Count;
        return new SendGroup(md, sentAt, count, first ?? "", images, readable);
    }

    // 入れ直してよい画像: 保存先の中にある .png で、いま在るもの（E-08。7 日の自動削除で消えたもの・保存先の外のファイルは入れない）
    public static List<string> UsableImages(SendGroup g, string saveDir)
    {
        var list = new List<string>();
        foreach (var p in g.ImagePaths)
            if (SnipFiles.IsUsableImage(p, saveDir) && File.Exists(p)) list.Add(Path.GetFullPath(p));   // reparse point を経由するものも入れない（R-23）
        return list;
    }
}

// 履歴の窓の選び方（R-21・E-08）。画面から切り離して単体テストで確かめる
public static class HistoryPick
{
    // 選べるのは読める行だけ（E-08）。それ以外は null
    public static SendGroup? Choose(IReadOnlyList<SendGroup> groups, int index) =>
        index >= 0 && index < groups.Count && groups[index].Readable ? groups[index] : null;

    // 読めない行に選択が移ったときの行き先。キーで動いたら同じ向きの次の読める行へ、無ければ元の行へ。
    // マウスで押したときは元の行のまま（押していない隣の送信を選ばない。verify V2）
    public static int Adjust(IReadOnlyList<SendGroup> groups, int index, int last, bool byMouse)
    {
        if (index < 0 || index >= groups.Count || groups[index].Readable) return index;
        if (byMouse) return last;
        var step = index >= last ? 1 : -1;
        for (int j = index + step; j >= 0 && j < groups.Count; j += step)
            if (groups[j].Readable) return j;
        return last;
    }

    // 一覧の行の見出し。入れられる枚数を出し、一部の画像が消えていれば送ったときの件数も添える（verify M2）
    public static string Head(SendGroup g)
    {
        var at = g.SentAt.ToString("M/d HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        if (!g.Readable) return $"{at}　{g.Count} 件　（読めません）";
        return g.Present.Count < g.Count ? $"{at}　{g.Present.Count} 件（送ったときは {g.Count} 件）" : $"{at}　{g.Count} 件";
    }

    // 入れ直した後の知らせ（R-21 の文言）
    public static string Message(ResendResult r) => r.Outcome switch
    {
        ResendOutcome.Copied => $"{r.ImageCount} 件をクリップボードに入れました。Ctrl＋V で貼れます",
        ResendOutcome.Unreadable => "この送信は画像が残っていないか、送る文を読めません",
        _ => "クリップボードに書けませんでした。もう一度選んでください",
    };
}

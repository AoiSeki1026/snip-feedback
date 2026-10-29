using System.Drawing;
using System.Text;

namespace SnipFeedback.Core;

// R-18: 文字認識の結果を送る文に入れる形に整える
public static class OcrText
{
    public const int MaxChars = 1000;
    public const string Ellipsis = "…（以下略）";
    public const string TimedOut = "（読み取りが間に合わなかった）";

    // 幅か高さが 100 画素未満の画像は Windows の文字認識が読まないことがあるので 2 倍にする（3・4 倍は読み違いが増えた）。
    // 2 倍で上限（OcrEngine.MaxImageDimension）を超えるなら拡大しない
    public const int SmallSide = 100;
    public static int ScaleFor(int width, int height, int maxDimension) =>
        (width < SmallSide || height < SmallSide) && width * 2 <= maxDimension && height * 2 <= maxDimension ? 2 : 1;

    // 行を並べ直して Clean にかける。1 文字だけの行（記号の読み違い）は省く。
    // 横の範囲が重なる行を同じ列とみなし（画面のサイドバー・本文・ターミナルなど）、列は左から、列の中は上から並べる。
    // 列の中で上端の差が高さの半分以内の行は同じ高さとみなし、左から並べる
    public static string Arrange(IEnumerable<OcrLineBox> lines)
    {
        var columns = new List<List<OcrLineBox>>();
        double right = double.MinValue;
        foreach (var l in lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)).OrderBy(l => l.Left))
        {
            if (columns.Count == 0 || l.Left >= right) { columns.Add(new List<OcrLineBox>()); right = double.MinValue; }
            columns[^1].Add(l);
            right = Math.Max(right, l.Left + l.Width);
        }
        var ordered = new List<OcrLineBox>();
        foreach (var col in columns)
        {
            var list = col.OrderBy(l => l.Top).ToList();
            int i = 0;
            while (i < list.Count)
            {
                var head = list[i];
                var row = list.Skip(i).TakeWhile(l => l.Top - head.Top <= Math.Max(1, head.Height / 2)).ToList();
                ordered.AddRange(row.OrderBy(l => l.Left));
                i += row.Count;
            }
        }
        var kept = ordered.Select(l => CleanLine(l.Text)).Where(t => t.Length > 1);
        return Clean(string.Join("\n", kept));
    }

    // Windows の文字認識は日本語の文字の間に空白を入れる。片側が日本語の文字で、どちらの側も英字でない空白を除く
    // （「エ ラ ー」→「エラー」、「5 で す」→「5です」、「ー Error」は残す）。行の区切りは残す
    public static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var lines = raw.Replace("\r\n", "\n").Split('\n')
            .Select(CleanLine).Where(l => l.Length > 0);
        var text = string.Join("\n", lines);
        return text.Length > MaxChars ? text[..MaxChars] + Ellipsis : text;
    }

    private static string CleanLine(string line)
    {
        var s = line.Trim();
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == ' ' || s[i] == '　')
            {
                int j = i; while (j < s.Length && (s[j] == ' ' || s[j] == '　')) j++;
                char l = sb.Length > 0 ? sb[^1] : ' ', r = j < s.Length ? s[j] : ' ';
                bool drop = (IsJapanese(l) || IsJapanese(r)) && !IsAsciiLetter(l) && !IsAsciiLetter(r);
                if (!drop) sb.Append(' ');
                i = j - 1;
                continue;
            }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    private static bool IsAsciiLetter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    // ひらがな・カタカナ・漢字・全角の記号
    private static bool IsJapanese(char c) =>
        c is >= '　' and <= 'ヿ'      // 和文の記号・ひらがな・カタカナ
          or >= '㐀' and <= '䶿'      // 漢字（拡張 A）
          or >= '一' and <= '鿿'      // 漢字
          or >= '＀' and <= '￯';     // 全角・半角形
}

// 文字認識の1行（画像の中の上端・左端・高さと文字）
public sealed record OcrLineBox(double Top, double Left, double Width, double Height, string Text);

// R-19: ブラウザのアドレス欄の文字列
public static class UrlText
{
    public static bool IsBrowser(string appName) =>
        appName.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase) || appName.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase);

    // UI Automation で見つけた編集欄がアドレス欄か。全画面表示やアプリのウィンドウでは最初の編集欄がページの入力欄の
    // ことがあるので、名前で確かめる（verify S1）。部分一致だと「メールアドレス」欄も通るので、既知の名前と完全一致で比べる（verify-b S16）
    private static readonly string[] AddressBarNames = { "アドレス検索バー", "アドレスと検索バー", "Address and search bar" };
    public static bool IsAddressBarName(string? name) =>
        !string.IsNullOrEmpty(name) && AddressBarNames.Any(n => string.Equals(n, name.Trim(), StringComparison.OrdinalIgnoreCase));

    // URL の形か（空白を含まず、. か : を含む）。検索中の言葉などは出さない（verify S1）
    public static bool LooksLikeUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 2048 && !value.Any(char.IsWhiteSpace) && (value.Contains('.') || value.Contains(':'));

    // クエリ文字列（? から # の前まで）を除く。空なら null
    // r8: # 以降（フラグメント）も外す。ただし #/ で始まる画面の道筋は残し、その中のクエリは外す（v1 の R-06 と同じ扱い）。
    // #access_token= のように秘密の値が入ることがあるため
    public static string? StripQuery(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        var h = s.IndexOf('#');
        var main = h >= 0 ? s[..h] : s;
        var frag = h >= 0 ? s[h..] : "";
        var q = main.IndexOf('?');
        if (q >= 0) main = main[..q];
        if (frag.StartsWith("#/"))
        {
            var fq = frag.IndexOf('?');
            return main + (fq >= 0 ? frag[..fq] : frag);
        }
        return main;
    }
}

// R-20: 浮かせた表示の位置
public static class BadgePlacement
{
    // 覚えた位置が作業領域に収まればそこ、無いか外れていれば右下（margin 空ける）
    public static Point Locate(Rectangle workArea, Size size, Point? saved, int margin)
    {
        var def = new Point(workArea.Right - size.Width - margin, workArea.Bottom - size.Height - margin);
        if (saved is not { } p) return def;
        return workArea.Contains(new Rectangle(p, size)) ? p : def;
    }
}

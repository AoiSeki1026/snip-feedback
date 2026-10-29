namespace SnipFeedback.Core;

// クリップボードへの書き込み（テストで失敗させられるように差し替え可能にする）。
// r7: ファイルの一覧として入れる（Code タブに貼ると画像は添付、.md は @ で参照される）
public interface IClipboardWriter
{
    bool TrySetFiles(IReadOnlyList<string> paths);
}

// SentButNotCleared: 送る文はクリップボードに入ったが、一覧を空にできなかった（verify-b V12）
// NotWritten: 送る文の .md を書けなかった（verify S6）
public enum SendOutcome { Sent, NothingToSend, SavedToFile, SentButNotCleared, NotWritten }

public sealed record SendResult(SendOutcome Outcome, string? FallbackPath = null, string? Error = null);

// 送信: 送る文を send-HHmmss-fff.md に書き、.md と画像をファイルとしてクリップボードに入れ、一覧を空にする（R-13〔r7〕・R-14、E-03、E-06）
public sealed class Sender
{
    public const int Tries = 3, WaitMs = 200;
    private readonly IClipboardWriter _clipboard;
    private readonly string _saveDir;
    private readonly Action<int> _sleep;

    public Sender(IClipboardWriter clipboard, string saveDir, Action<int> sleep)
    {
        _clipboard = clipboard; _saveDir = saveDir; _sleep = sleep;
    }

    public SendResult Send(SnipQueue queue, DateTime now)
    {
        // R-23: 保存先の外（または reparse point を経由する）項目は送らない。送る文にも載せず（パスを渡さない）、送った後も一覧に残す
        var items = queue.Items.Where(i => SnipFiles.IsUsableImage(i.ImagePath, _saveDir)).ToList();
        if (items.Count == 0) return new SendResult(SendOutcome.NothingToSend);   // E-06: クリップボードは変えない
        // r7: 送る文を .md に書き、その .md と画像をファイルとしてクリップボードへ（.md が先、画像は送る文の番号の順）
        // 名前にミリ秒まで付ける（同じ秒に 2 回送っても上書きしない。verify S11）
        var dir = Path.Combine(_saveDir, now.ToString("yyyyMMdd"));
        var md = Path.Combine(dir, $"send-{now:HHmmss-fff}.md");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(md, SendText.Build(items, now));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SendResult(SendOutcome.NotWritten, md, ex.Message);   // verify S6: 一覧は残す
        }
        var files = new List<string> { md };
        files.AddRange(items.Select(i => i.ImagePath).Where(File.Exists));   // 消えた画像は入れない（verify S7）
        for (int i = 0; i < Tries; i++)
        {
            if (i > 0) _sleep(WaitMs);
            if (_clipboard.TrySetFiles(files))
            {
                try { queue.RemoveSent(items); }
                catch (IOException) { return new SendResult(SendOutcome.SentButNotCleared, md); }
                return new SendResult(SendOutcome.Sent, md);
            }
        }
        // E-03: 3 回とも失敗したら .md の場所を知らせ、一覧は空にしない
        return new SendResult(SendOutcome.SavedToFile, md);
    }

    // 履歴から入れ直す（R-21・E-08、r9）。.md と、保存先の中に今在る画像を R-13 と同じ形で入れる。ためる一覧は変えない
    public ResendResult Resend(SendGroup group)
    {
        var images = group.Readable && File.Exists(group.MdPath) ? SendHistory.UsableImages(group, _saveDir) : new List<string>();
        if (images.Count == 0) return new ResendResult(ResendOutcome.Unreadable, 0);   // E-08: クリップボードは変えない
        var files = new List<string> { group.MdPath };
        files.AddRange(images);
        for (int i = 0; i < Tries; i++)
        {
            if (i > 0) _sleep(WaitMs);
            if (_clipboard.TrySetFiles(files)) return new ResendResult(ResendOutcome.Copied, images.Count);
        }
        return new ResendResult(ResendOutcome.Failed, images.Count);
    }
}

public enum ResendOutcome { Copied, Unreadable, Failed }

public sealed record ResendResult(ResendOutcome Outcome, int ImageCount);

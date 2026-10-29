using System.Drawing.Imaging;
using System.Windows.Automation;
using SnipFeedback.Core;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace SnipFeedback;

// R-18: Windows の文字認識（日本語）で切り取った画像の文字を読む。端末の中だけで処理する（N-01）
internal static class OcrReader
{
    // 1 度だけ作る。作るときの例外は覚えず次に作り直す（PublicationOnly）。認識は 1 つずつ（verify S10）
    private static readonly Lazy<OcrEngine?> _engine = new(() =>
        OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("ja")) ?? OcrEngine.TryCreateFromUserProfileLanguages(),
        LazyThreadSafetyMode.PublicationOnly);
    private static readonly SemaphoreSlim _one = new(1, 1);

    // 受け取った画像は呼び出し側から切り離したもの（このメソッドが解放する）。読めなければ空
    public static Task<string> ReadAsync(Bitmap image) => Task.Run(async () =>
    {
        using (image)
        {
            var engine = Engine();
            if (engine == null) return "";
            if (image.Width > OcrEngine.MaxImageDimension || image.Height > OcrEngine.MaxImageDimension) return "";
            var k = OcrText.ScaleFor(image.Width, image.Height, (int)OcrEngine.MaxImageDimension);
            using var scaled = k == 1 ? null : new Bitmap(image, image.Width * k, image.Height * k);
            using var ms = new MemoryStream();
            (scaled ?? image).Save(ms, ImageFormat.Bmp);
            ms.Position = 0;
            var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
            using var sb = await decoder.GetSoftwareBitmapAsync();
            await _one.WaitAsync();
            Windows.Media.Ocr.OcrResult result;
            try { result = await engine.RecognizeAsync(sb); } finally { _one.Release(); }
            // 行は上から順とは限らないので、各行の言葉の枠から位置を取って並べ直す
            return OcrText.Arrange(result.Lines.Where(l => l.Words.Count > 0).Select(l => new OcrLineBox(
                l.Words.Min(w => w.BoundingRect.Y), l.Words.Min(w => w.BoundingRect.X),
                l.Words.Max(w => w.BoundingRect.X + w.BoundingRect.Width) - l.Words.Min(w => w.BoundingRect.X),
                l.Words.Max(w => w.BoundingRect.Height), l.Text)));
        }
    });

    private static OcrEngine? Engine() => _engine.Value;
}

// R-19: Chrome・Edge のアドレス欄の文字列を UI Automation で読む（読むだけ。操作しない）
internal static class UrlReader
{
    public static Task<string?> ReadAsync(IntPtr hwnd) => Task.Run(() =>
    {
        var root = AutomationElement.FromHandle(hwnd);
        var edit = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
        // アドレス欄の名前で、値が URL の形のときだけ使う。ページの入力欄や検索中の言葉は出さない（verify S1）
        if (edit == null || !UrlText.IsAddressBarName(edit.Current.Name)) return null;
        if (!edit.TryGetCurrentPattern(ValuePattern.Pattern, out var vp)) return null;
        var value = ((ValuePattern)vp).Current.Value;
        return UrlText.LooksLikeUrl(value) ? UrlText.StripQuery(value) : null;
    });
}

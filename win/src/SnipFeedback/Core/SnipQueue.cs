using System.Text.Json;

namespace SnipFeedback.Core;

// ためる一覧。queue.json に書き、起動時に復元する（R-11・R-12・R-16）
public sealed class SnipQueue
{
    private readonly string _path;
    private readonly List<SnipItem> _items = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public IReadOnlyList<SnipItem> Items => _items;
    public event Action? Changed;
    // 起動時に queue.json を開けなかった理由（空の一覧で始める。verify-b V15）
    public string? LoadError { get; private set; }
    // 読めなかったときは一覧の中身が分からないので、古い画像を自動で消さない（verify-c V21）
    public bool CanCleanup => LoadError == null;
    // 読めなかった元の queue.json を、最初に上書きする前に複製して残す（verify-c V21）
    private bool _keepOriginal;

    public SnipQueue(string path)
    {
        _path = path;
        Load();
    }

    // 書けなければ例外を投げ、一覧は変えない（E-02）
    public void Add(SnipItem item)
    {
        _items.Add(item);
        try { Write(); }
        catch { _items.RemoveAt(_items.Count - 1); throw; }
        Changed?.Invoke();
    }

    // 1件消す。その画像も消す（R-12）
    public void Remove(int index)
    {
        var it = _items[index];
        _items.RemoveAt(index);
        try { Write(); }
        catch { _items.Insert(index, it); throw; }   // 書けなければ一覧も画像も元のまま（verify-b V16）
        // 保存先（queue.json のあるフォルダ）の中の画像だけ消す。書き換えられた queue.json で外のファイルを消さないように（R-23）
        if (SnipFiles.IsUsableImage(it.ImagePath, Path.GetDirectoryName(Path.GetFullPath(_path))!))
            try { File.Delete(it.ImagePath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        Changed?.Invoke();
    }

    // 送った項目だけを外す。画像は残す（R-15 の 7 日間）。送らなかった項目（保存先の外。R-23）は残す。書けなければ元のまま
    public void RemoveSent(IReadOnlyCollection<SnipItem> sent)
    {
        var saved = _items.ToList();
        _items.RemoveAll(i => sent.Any(s => ReferenceEquals(s, i)));
        try { Write(); }
        catch { _items.Clear(); _items.AddRange(saved); throw; }
        Changed?.Invoke();
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        List<SnipItem>? loaded;
        try { loaded = JsonSerializer.Deserialize<List<SnipItem>>(File.ReadAllText(_path)); }
        catch (JsonException)
        {
            // 壊れていたら空として始め、元は退避する（消さない）。自動で消す期限は退避した日から数える（verify-b V17）
            var broken = _path + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            try { File.Move(_path, broken); File.SetLastWriteTime(broken, DateTime.Now); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LoadError = ex.Message; _keepOriginal = true; }
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LoadError = ex.Message;   // 開けなくても常駐は始める（verify-b V15）
            _keepOriginal = true;
            return;
        }
        // 画像が無くなった項目は復元しない。欠けた値は空・「不明」として読む（verify V5）
        if (loaded != null)
            _items.AddRange(loaded
                .Where(i => i != null && !string.IsNullOrEmpty(i.ImagePath) && File.Exists(i.ImagePath))
                .Select(i => i with
                {
                    Comment = i.Comment ?? "",
                    Boxes = (i.Boxes ?? new List<SnipBox>()).Where(b => b != null).Select(b => b with { Comment = b.Comment ?? "" }).ToList(),
                    AppName = string.IsNullOrEmpty(i.AppName) ? "不明" : i.AppName,
                    WindowTitle = string.IsNullOrEmpty(i.WindowTitle) ? "不明" : i.WindowTitle,
                }));
    }

    // 一時ファイルに書いて置き換える（書きかけで壊れないように）。
    // 書けない理由（容量・権限）を問わず IOException で返す（呼び出し側は E-02 として1種類で受ける）
    private void Write()
    {
        var tmp = _path + ".tmp";
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (_keepOriginal && File.Exists(_path))
            {
                // 複製できなければ（まだ開けないなら）書かずに断る。元の一覧を上書きで失わない
                var kept = _path + ".unread-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                File.Copy(_path, kept, overwrite: false);
                File.SetLastWriteTime(kept, DateTime.Now);   // 自動で消す期限は残した日から数える（V17 と同じ）
                _keepOriginal = false;
            }
            File.WriteAllText(tmp, JsonSerializer.Serialize(_items, Json));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            TryDelete(tmp);
            throw new IOException($"ためる一覧を書けません: {_path}", ex);
        }
        catch (IOException)
        {
            TryDelete(tmp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

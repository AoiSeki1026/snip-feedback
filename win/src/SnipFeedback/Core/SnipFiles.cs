namespace SnipFeedback.Core;

// 送る・入れ直す・一覧から消す・自動で消すときに扱ってよいファイルか（R-23、r14、H-M42）。
// 保存先の中にあり、保存先からそのファイルまでの途中（フォルダとファイル自身）に reparse point（ジャンクション・シンボリックリンク）を含まないもの。
// queue.json を書き換えられたり、保存先にジャンクションを置かれたりしても、保存先の外のファイルを送る・消すことにならないように
public static class SnipFiles
{
    public static bool IsUsableImage(string? path, string saveDir) => IsInside(path, saveDir, ".png");

    public static bool IsInside(string? path, string saveDir, string extension)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0')) return false;
        string full, root;
        try
        {
            if (!Path.IsPathFullyQualified(path)) return false;
            full = Path.GetFullPath(path);
            root = Path.GetFullPath(saveDir).TrimEnd(Path.DirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(Path.GetExtension(full), extension, StringComparison.OrdinalIgnoreCase)) return false;
        // ファイル自身と、保存先より下のフォルダを 1 つずつ確かめる（保存先そのものは利用者が選んだ場所なので問わない）
        for (var p = full; p.Length > root.Length; p = Path.GetDirectoryName(p) ?? root)
            if (IsReparsePoint(p)) return false;
        return true;
    }

    // 在るものが reparse point なら true。無い・読めないものは false（在るかどうかは呼び出し側が確かめる）
    public static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }
}

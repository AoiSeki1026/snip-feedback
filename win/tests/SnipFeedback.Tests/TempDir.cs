namespace SnipFeedback.Tests;

internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "snipfb-test-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public string File(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}

namespace SnipFeedback.Tests;

// N-01: 画像・一言をネットワークへ送らない。ネットワークのアセンブリを参照していないことで確かめる（実機の通信 0 は AC-11 で手動）
public class NoNetworkTests
{
    [Fact]
    public void 本体はネットワークのアセンブリを参照しない()
    {
        var banned = new[] { "System.Net.Http", "System.Net.Sockets", "System.Net.Requests", "System.Net.WebClient", "System.Net.WebSockets", "System.Net.Mail" };
        var refs = typeof(TrayApp).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.Empty(refs.Intersect(banned));
    }
}

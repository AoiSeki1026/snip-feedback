namespace SnipFeedback.Tests;

// T-01: 本体を参照してテストが動くこと
public class ScaffoldTests
{
    [Fact]
    public void 本体のアセンブリを参照できる()
    {
        Assert.Equal("SnipFeedback", typeof(TrayApp).Assembly.GetName().Name);
    }
}

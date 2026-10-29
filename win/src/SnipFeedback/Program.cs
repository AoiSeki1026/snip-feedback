namespace SnipFeedback;

// 画面切り取りフィードバック（v2）。常駐してタスクトレイにアイコンを出す（docs/design.md）
internal static class Program
{
    internal const string AppName = "画面切り取りフィードバック";

    // H-M33: アプリのアイコン（app.ico を埋め込みから）。size を指定すると、その大きさに最も近い絵を選ぶ
    internal static Icon AppIcon(int size = 32)
    {
        using var s = typeof(Program).Assembly.GetManifestResourceStream("SnipFeedback.app.ico")!;
        return new Icon(s, size, size);
    }

    [STAThread]
    static void Main()
    {
        // 多重起動を防ぐ（design.md「起動」）
        using var mutex = new Mutex(true, @"Local\SnipFeedback", out bool first);
        if (!first) return;
        // トレイの「終了」で次のサインインまで止めている間は、見張りの合図で起動されてもすぐ終わる（H-M35 1a、要件 r11）
        if (new Core.PauseFlag().IsSet) return;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);   // verify V2
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}

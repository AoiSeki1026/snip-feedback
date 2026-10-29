namespace SnipFeedback;

// 起動キーを受ける見えないウィンドウ。RegisterHotKey は前面のアプリから焦点を奪わない（design.md 方針）
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int Id = 1;
    public event Action? Pressed;
    public bool Registered { get; }
    public int Error { get; }

    public HotkeyWindow(uint modifiers, Keys key)
    {
        CreateHandle(new CreateParams());
        Registered = Native.RegisterHotKey(Handle, Id, modifiers | Native.MOD_NOREPEAT, (uint)key);
        if (!Registered) Error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && m.WParam == (IntPtr)Id) Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Registered) Native.UnregisterHotKey(Handle, Id);
        DestroyHandle();
    }
}

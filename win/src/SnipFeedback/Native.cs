using System.Runtime.InteropServices;
using System.Text;

namespace SnipFeedback;

// Windows API の呼び出し（design.md: 追加のライブラリなしで直接呼ぶ）
internal static class Native
{
    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr h);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    // 入力欄（EDIT）: 右の余白（▼▲ の分）と、先頭の行へ戻すスクロール（R-22、H-M40）
    public const int EM_SETMARGINS = 0x00D3, EC_RIGHTMARGIN = 0x2, EM_LINESCROLL = 0x00B6;
    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;
    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);
    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

    public const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;
    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int sx, int sy, uint rop);
}

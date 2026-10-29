# 案内の窓（Edge の --app 窓）に専用の AppUserModelID とアイコンを付け、タスクバーでほかの 127.0.0.1 のツールと分ける（H-M38、r16 R-23）
# serve.mjs が Edge を開いた直後に、切り離さずに隠して起動する（detached だとコンソール無しで付けられない）。
# 窓: タイトルが -Title で始まり、通常のタブ（「 - Microsoft Edge」「 - Microsoft Edge Beta」等で終わる）ではない msedge.exe の窓。
# -WatchPid を付けると、そのプロセス（配布サーバ）が動いている間ずっと見張り、新しい窓や Edge が ID を付け直した窓にも付け直す（verify-h-m38 N1）。
# 付けないときは、窓を見つけてから -HoldSeconds 秒で終わる（2 回目の起動〔ALREADY〕用）。付けられなくても案内の窓はそのまま使える
# -CheckTitle は試験用: 窓には触らず、渡したタイトルが案内の窓と判定されるかだけを「1 / 0」で 1 行ずつ出す
param(
  [Parameter(Mandatory)] [string]$Title,
  [string]$Icon,
  [string]$Command,
  [string]$AppId = 'FeedbackSystem.Guide',
  [int]$WaitSeconds = 15,
  [int]$HoldSeconds = 5,
  [int]$WatchPid = 0,
  [string[]]$CheckTitle
)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System; using System.Collections.Generic; using System.Diagnostics; using System.Runtime.InteropServices; using System.Text; using System.Text.RegularExpressions;
public static class FbWindowId {
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr h, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore ps);
  // HRESULT をそのまま受け取る（PreserveSig。verify-h-m38 N2）
  [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IPropertyStore {
    [PreserveSig] int GetCount(out uint c); [PreserveSig] int GetAt(uint i, out PK k); [PreserveSig] int GetValue(ref PK k, out PV v);
    [PreserveSig] int SetValue(ref PK k, ref PV v); [PreserveSig] int Commit();
  }
  [StructLayout(LayoutKind.Sequential, Pack = 4)] struct PK { public Guid f; public uint p; }
  [StructLayout(LayoutKind.Sequential)] struct PV { public ushort vt; ushort a, b, c; public IntPtr p; IntPtr q; }
  [DllImport("ole32.dll")] static extern int PropVariantClear(ref PV v);
  static readonly Guid Fmt = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");   // PKEY_AppUserModel_*
  const uint RelaunchCommand = 2, RelaunchIcon = 3, RelaunchName = 4, Id = 5;
  // 通常のタブの窓のタイトルの終わり（「Microsoft​ Edge」はゼロ幅の空白入り。Beta・Dev・Canary も。verify-h-m38 N4）
  static readonly Regex NormalTab = new Regex(@" - Microsoft\W*Edge(\W+(Beta|Dev|Canary))?$");

  public static bool IsGuideTitle(string t, string title) {
    return t != null && t.StartsWith(title, StringComparison.Ordinal) && !NormalTab.IsMatch(t);
  }

  public static List<IntPtr> Find(string title) {
    var found = new List<IntPtr>();
    EnumWindows((h, l) => {
      if (!IsWindowVisible(h)) return true;
      var sb = new StringBuilder(512); GetWindowText(h, sb, 512);
      if (!IsGuideTitle(sb.ToString(), title)) return true;
      uint pid; GetWindowThreadProcessId(h, out pid);
      try { using (var p = Process.GetProcessById((int)pid)) if (!string.Equals(p.ProcessName, "msedge", StringComparison.OrdinalIgnoreCase)) return true; } catch { return true; }
      found.Add(h); return true;
    }, IntPtr.Zero);
    return found;
  }

  static IPropertyStore Store(IntPtr h) { var iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"); IPropertyStore s; Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(h, ref iid, out s)); return s; }

  public static string GetId(IntPtr h) {
    var s = Store(h);
    try {
      var k = new PK { f = Fmt, p = Id }; PV v;
      Marshal.ThrowExceptionForHR(s.GetValue(ref k, out v));
      try { return v.vt == 31 ? Marshal.PtrToStringUni(v.p) : null; } finally { PropVariantClear(ref v); }
    } finally { Marshal.ReleaseComObject(s); }
  }

  // 起こし直しのコマンド・表示名・アイコンを先に、ID を最後に付ける（ID を変えた時点でタスクバーが読み直すため）
  public static void Set(IntPtr h, string id, string icon, string command, string name) {
    var s = Store(h);
    try {
      Put(s, RelaunchCommand, command); Put(s, RelaunchName, name); Put(s, RelaunchIcon, icon + ",0"); Put(s, Id, id);
      Marshal.ThrowExceptionForHR(s.Commit());
    } finally { Marshal.ReleaseComObject(s); }
  }

  static void Put(IPropertyStore s, uint pid, string text) {
    var k = new PK { f = Fmt, p = pid };
    var v = new PV { vt = 31, p = Marshal.StringToCoTaskMemUni(text) };
    try { Marshal.ThrowExceptionForHR(s.SetValue(ref k, ref v)); } finally { Marshal.FreeCoTaskMem(v.p); }
  }
}
'@

if ($CheckTitle) { foreach ($t in $CheckTitle) { if ([FbWindowId]::IsGuideTitle($t, $Title)) { '1' } else { '0' } }; exit 0 }
if (-not $Icon -or -not $Command) { throw '-Icon と -Command が要ります' }

function Alive { if ($WatchPid -le 0) { return $false }; return [bool](Get-Process -Id $WatchPid -ErrorAction SilentlyContinue) }

$deadline = (Get-Date).AddSeconds($WaitSeconds)
$holdUntil = $null
while ($true) {
  foreach ($h in [FbWindowId]::Find($Title)) {
    try {
      if ([FbWindowId]::GetId($h) -ne $AppId) {
        [FbWindowId]::Set($h, $AppId, $Icon, $Command, $Title)
        Write-Output "付けた: $h"
      }
      if (-not $holdUntil) { $holdUntil = (Get-Date).AddSeconds($HoldSeconds) }
    } catch { Write-Output "付けられない: $h $($_.Exception.Message)" }
  }
  if (Alive) { Start-Sleep -Seconds 2; continue }   # 配布サーバが動いている間は見張り続ける
  if ($WatchPid -gt 0) { break }                     # 配布サーバが終わった
  if ($holdUntil) { if ((Get-Date) -ge $holdUntil) { break } }
  elseif ((Get-Date) -ge $deadline) { Write-Output "窓が見つからない: $Title"; exit 1 }
  Start-Sleep -Milliseconds 300
}
exit 0

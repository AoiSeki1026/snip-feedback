// 試験用の偽の msedge.exe（T-20、verify-h-m38-b N8）。test/window-id.spec.mjs が Add-Type -OutputAssembly で
// msedge.exe という名前に組み立て、Edge なしで案内の窓の見張り（set-window-id.ps1）を試す。
// 窓は画面の外に置き、前面に出さず（WS_EX_NOACTIVATE）、タスクバーにも出さない（使っている人の操作を邪魔しない）。
// 標準入力の 1 行ごとに答える: get → 今の AppUserModelID（無ければ空行） ／ set <ID> → ID を書き換えて ok ／ 入力が閉じたら終わる
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

public static class FakeEdge
{
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint c); [PreserveSig] int GetAt(uint i, out PK k); [PreserveSig] int GetValue(ref PK k, out PV v);
        [PreserveSig] int SetValue(ref PK k, ref PV v); [PreserveSig] int Commit();
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)] struct PK { public Guid f; public uint p; }
    [StructLayout(LayoutKind.Sequential)] struct PV { public ushort vt; ushort a, b, c; public IntPtr p; IntPtr q; }
    [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr h, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore ps);
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PV v);
    static readonly Guid Fmt = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    static IPropertyStore Store(IntPtr h)
    {
        var iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"); IPropertyStore s;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(h, ref iid, out s)); return s;
    }

    static string GetId(IntPtr h)
    {
        var s = Store(h);
        try
        {
            var k = new PK { f = Fmt, p = 5 }; PV v;
            Marshal.ThrowExceptionForHR(s.GetValue(ref k, out v));
            try { return v.vt == 31 ? Marshal.PtrToStringUni(v.p) : ""; } finally { PropVariantClear(ref v); }
        }
        finally { Marshal.ReleaseComObject(s); }
    }

    static void SetId(IntPtr h, string id)
    {
        var s = Store(h);
        try
        {
            var k = new PK { f = Fmt, p = 5 };
            var v = new PV { vt = 31, p = Marshal.StringToCoTaskMemUni(id) };
            try { Marshal.ThrowExceptionForHR(s.SetValue(ref k, ref v)); } finally { Marshal.FreeCoTaskMem(v.p); }
            Marshal.ThrowExceptionForHR(s.Commit());
        }
        finally { Marshal.ReleaseComObject(s); }
    }

    sealed class QuietForm : Form
    {
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= 0x08000000; return p; }   // WS_EX_NOACTIVATE
        }
    }

    [STAThread]
    public static void Main(string[] args)
    {
        var f = new QuietForm
        {
            Text = args[0], ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000), Size = new Size(160, 90),
        };
        f.Shown += delegate
        {
            var h = f.Handle;
            Reply("ready");
            var t = new Thread(delegate ()
            {
                string line;
                while ((line = Console.In.ReadLine()) != null)
                {
                    var cmd = line;
                    string answer;
                    try
                    {
                        answer = (string)f.Invoke(new Func<string>(delegate
                        {
                            if (cmd == "get") return GetId(h);
                            if (cmd.StartsWith("set ")) { SetId(h, cmd.Substring(4)); return "ok"; }
                            return "?";
                        }));
                    }
                    catch (Exception ex) { answer = "error " + ex.Message; }
                    Reply(answer);
                }
                f.BeginInvoke(new Action(f.Close));
            });
            t.IsBackground = true;
            t.Start();
        };
        Application.Run(f);
    }

    static void Reply(string s) { Console.Out.WriteLine(s); Console.Out.Flush(); }
}

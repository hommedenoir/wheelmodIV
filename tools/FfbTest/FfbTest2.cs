// Standalone, SELF-MEASURING force feedback test for the GTA IV Wheel Mod.
// Uses the mod's own ForceFeedback.cs outside GTA IV and tries several ways of making the
// wheel move. It measures by itself whether the wheel turned (via the steering axis),
// so nobody has to feel anything: just keep your hands OFF the wheel.
//
// Results are printed and also saved to FfbTest_results.txt next to this program.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

class FfbTest
{
    // ---------------- steering position via winmm (independent of DirectInput) ----------------
    [StructLayout(LayoutKind.Sequential)]
    struct JOYINFOEX
    {
        public int dwSize, dwFlags;
        public int dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos;
        public int dwButtons, dwButtonNumber, dwPOV, dwReserved1, dwReserved2;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct JOYCAPSW
    {
        public ushort wMid, wPid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
        public int wXmin, wXmax, wYmin, wYmax, wZmin, wZmax;
        public int wNumButtons, wPeriodMin, wPeriodMax;
        public int wRmin, wRmax, wUmin, wUmax, wVmin, wVmax;
        public int wCaps, wMaxAxes, wNumAxes, wMaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szRegKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szOEMVxD;
    }
    [DllImport("winmm.dll")] static extern int joyGetPosEx(int id, ref JOYINFOEX pji);
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern int joyGetDevCapsW(int id, ref JOYCAPSW pjc, int size);
    [DllImport("winmm.dll")] static extern int joyGetNumDevs();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);

    static int joyId = -1, xMin = 0, xMax = 65535;
    const float WheelDegrees = 900f;

    static bool FindWheel()
    {
        int n = Math.Min(joyGetNumDevs(), 16);
        for (int id = 0; id < n; id++)
        {
            JOYCAPSW c = new JOYCAPSW();
            if (joyGetDevCapsW(id, ref c, Marshal.SizeOf(typeof(JOYCAPSW))) != 0) continue;
            if (c.wMid != 0x044F || c.wPid != 0xB696) continue;
            JOYINFOEX j = new JOYINFOEX();
            j.dwSize = Marshal.SizeOf(typeof(JOYINFOEX)); j.dwFlags = 0xFF;
            if (joyGetPosEx(id, ref j) != 0) continue;
            joyId = id; xMin = c.wXmin; xMax = c.wXmax;
            return true;
        }
        return false;
    }

    // steering position in degrees from centre (approx.)
    static float SteerDeg()
    {
        JOYINFOEX j = new JOYINFOEX();
        j.dwSize = Marshal.SizeOf(typeof(JOYINFOEX)); j.dwFlags = 0xFF;
        if (joyId < 0 || joyGetPosEx(joyId, ref j) != 0) return float.NaN;
        float f = (float)(j.dwXpos - xMin) / Math.Max(1, xMax - xMin);
        return (f - 0.5f) * WheelDegrees;
    }

    // ---------------- helpers ----------------
    static Form window;
    static StringBuilder log = new StringBuilder();

    static void Say(string s)
    {
        Console.WriteLine(s);
        log.AppendLine(s);
    }

    static byte[] Ints(params int[] v)
    {
        byte[] b = new byte[v.Length * 4];
        for (int i = 0; i < v.Length; i++) BitConverter.GetBytes(v[i]).CopyTo(b, i * 4);
        return b;
    }

    // Runs everyFrame repeatedly for ms milliseconds and measures how far the wheel moved
    static string Measure(int ms, Action everyFrame)
    {
        float start = SteerDeg(), lo = start, hi = start;
        int end = Environment.TickCount + ms;
        while (Environment.TickCount < end)
        {
            if (everyFrame != null) everyFrame();
            Application.DoEvents();
            float d = SteerDeg();
            if (!float.IsNaN(d)) { if (d < lo) lo = d; if (d > hi) hi = d; }
            Thread.Sleep(10);
        }
        float endPos = SteerDeg();
        float range = hi - lo;
        return string.Format("moved {0:0.0} deg (start {1:0.0}, end {2:0.0}, range {3:0.0} .. {4:0.0})  => {5}",
            range, start, endPos, lo, hi, range >= 3f ? "MOVED" : "no movement");
    }

    static WheelMod.ForceFeedback Open(string why)
    {
        WheelMod.ForceFeedback f = new WheelMod.ForceFeedback();
        window.Activate(); SetForegroundWindow(window.Handle); Application.DoEvents();
        bool ok = f.Init(0x044F, 0xB696, window.Handle);
        Say("  open (" + why + "): " + (ok ? "OK" : "FAILED - " + f.Status) + "   state: " + f.DeviceState());
        return ok ? f : null;
    }

    static readonly Guid GUID_ConstantForce = new Guid("13541C20-8E33-11D0-9AD0-00A0C9A06E35");
    static readonly Guid GUID_Sine = new Guid("13541C23-8E33-11D0-9AD0-00A0C9A06E35");
    static readonly Guid GUID_Spring = new Guid("13541C27-8E33-11D0-9AD0-00A0C9A06E35");

    // ---------------- variants (round 2) ----------------
    // Round 1 showed: an effect started once moves the wheel, the per-frame update (old mod code) does not.
    // Each variant pushes one way for 2 s, then the other way for 2 s.
    static void Variant(string title, bool restart, bool dir, int duration, int createMag, bool update)
    {
        Say(title);
        WheelMod.ForceFeedback f = new WheelMod.ForceFeedback();
        f.RestartEachUpdate = restart;
        f.SendDirectionEachUpdate = dir;
        f.EffectDurationUs = duration;
        f.CreateMagnitude = createMag;
        window.Activate(); SetForegroundWindow(window.Handle); Application.DoEvents();
        bool ok = f.Init(0x044F, 0xB696, window.Handle);
        Say("  open: " + (ok ? "OK" : "FAILED - " + f.Status) + "   effect: " + f.EffectStatus());
        if (!ok) return;
        if (update)
        {
            Say("  push +0.6: " + Measure(2000, () => f.SetForce(0.6f)) + "   effect: " + f.EffectStatus());
            Say("  push -0.6: " + Measure(2000, () => f.SetForce(-0.6f)) + "   effect: " + f.EffectStatus());
            Say("  codes: " + f.Diag);
        }
        else
        {
            Say("  no updates: " + Measure(2500, null) + "   effect: " + f.EffectStatus());
        }
        f.SetForce(0f);
        f.Dispose();
    }

    [STAThread]
    static int Main()
    {
        Say("Self-measuring force feedback test ROUND 2 - running as " + (IntPtr.Size == 8 ? "64-bit" : "32-bit") + ", " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Say("Close the Thrustmaster Control Panel and GTA IV first.");
        Say("Put the wheel roughly in the centre and take your hands OFF it. Press Enter to start...");
        Console.ReadLine();

        if (!FindWheel()) { Say("T248 not found via winmm - is it connected and in PC mode?"); Console.ReadLine(); return 1; }
        Say("Wheel found (winmm id " + joyId + "). Starting position: " + SteerDeg().ToString("0.0") + " deg");

        window = new Form();
        window.Text = "FFB test window (leave open)";
        window.Width = 360; window.Height = 120;
        window.Show();
        Application.DoEvents();

        const int INF = -1;
        Say("");
        Variant("=== A: endless effect created with force 0.6, never updated ===", false, false, INF, 6000, false);
        Say("");
        Variant("=== B: endless effect, force changed every frame WITHOUT restart (new mod code) ===", false, false, INF, 0, true);
        Say("");
        Variant("=== C: like B but also re-sending the direction ===", false, true, INF, 0, true);
        Say("");
        Variant("=== D: effect lasting 30 minutes instead of endless, changed without restart ===", false, false, 1800000000, 0, true);
        Say("");
        Variant("=== E: 30-minute effect, restarted every frame ===", true, false, 1800000000, 0, true);
        Say("");
        Variant("=== F: old mod code (endless, restart every frame) - expected to fail ===", true, true, INF, 0, true);

        window.Close();
        Say("");
        Say("Done. Every line that says MOVED made the wheel turn by itself.");
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FfbTest2_results.txt");
        try { File.WriteAllText(path, log.ToString()); Console.WriteLine("Saved to " + path); } catch { }
        Console.WriteLine("Press Enter to exit.");
        Console.ReadLine();
        return 0;
    }
}

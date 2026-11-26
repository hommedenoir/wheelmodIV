// WheelInputTest - IV-SDK .NET script for GTA IV
// Guided input test: tells you what to press, detects which axis/button reacted,
// and saves the results to "WheelInputTest_results.txt" next to the script.
// Read-only: it does not change any game behaviour.
//
// F9  = show/hide the window
// F10 = skip the current step (e.g. if you don't have a clutch)
// F11 = restart the test

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

using IVSDKDotNet;
using IVSDKDotNet.Enums;

namespace WheelInputTest
{
    public class Main : Script
    {
        // ---------------- winmm joystick API ----------------
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

        static readonly string[] AxisNames = { "X", "Y", "Z", "R", "U", "V" };

        class Device
        {
            public int Id;
            public string Name;
            public ushort Vid, Pid;
            public int NumAxes, NumButtons;
            public int[] Min = new int[6], Max = new int[6], Axes = new int[6];
            public int Buttons;
            public int Pov;
        }

        // ---------------- guided test ----------------
        enum StepKind { Axis, Button }

        class Step
        {
            public string Prompt, Key;
            public StepKind Kind;
            public bool NeedsRelease; // pedals/buttons spring back, the wheel doesn't
            public Step(string key, string prompt, StepKind kind, bool needsRelease)
            { Key = key; Prompt = prompt; Kind = kind; NeedsRelease = needsRelease; }
        }

        readonly Step[] steps =
        {
            new Step("SteerLeft",  "Turn the wheel fully to the LEFT and hold it",      StepKind.Axis,   false),
            new Step("SteerRight", "Now turn the wheel fully to the RIGHT and hold it", StepKind.Axis,   false),
            new Step("Center",     "Put the wheel back to the CENTER, then press F10",  StepKind.Axis,   false),
            new Step("Throttle",   "Press the THROTTLE (gas) pedal all the way down",   StepKind.Axis,   true),
            new Step("Brake",      "Press the BRAKE pedal all the way down",            StepKind.Axis,   true),
            new Step("Clutch",     "Press the CLUTCH pedal all the way down (F10 if you have none)", StepKind.Axis, true),
            new Step("ShiftUp",    "Press the RIGHT paddle (shift up)",                 StepKind.Button, true),
            new Step("ShiftDown",  "Press the LEFT paddle (shift down)",                StepKind.Button, true),
        };

        const float AxisThreshold = 0.35f;  // fraction of the axis range that counts as "moved"
        const int HoldMs = 600;             // how long a movement must be held

        List<Device> devices = new List<Device>();
        Dictionary<int, int[]> baseAxes = new Dictionary<int, int[]>();
        Dictionary<int, int> baseButtons = new Dictionary<int, int>();
        List<string> results = new List<string>();

        int stepIndex = 0;
        bool waitingForRelease = false;
        int candidateDev = -1, candidateIdx = -1, candidateSince = 0, candidatePeak = 0;
        string lastResult = "";
        string resultsPath;
        int restartTime = 0;

        bool showWindow = true;
        bool windowOpen = true;

        public Main()
        {
            Initialized += Main_Initialized;
            KeyDown += Main_KeyDown;
            OnImGuiRendering += Main_OnImGuiRendering;
        }

        void Main_Initialized(object sender, EventArgs e)
        {
            string dir = Path.GetDirectoryName(GetFullPath());
            resultsPath = Path.Combine(dir, "WheelInputTest_results.txt");
            RegisterConsoleCommand("wheeltest", args => { showWindow = !showWindow; });
            IVGame.Console.Print("WheelInputTest loaded. F9 = window, F10 = skip step, F11 = restart.");
            Restart();
        }

        void Main_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F9) showWindow = !showWindow;
            else if (e.KeyCode == Keys.F10) SkipStep();
            else if (e.KeyCode == Keys.F11) Restart();
        }

        // ---------------- device polling ----------------
        void Poll()
        {
            devices.Clear();
            int count = Math.Min(joyGetNumDevs(), 16);
            for (int id = 0; id < count; id++)
            {
                JOYINFOEX info = new JOYINFOEX();
                info.dwSize = Marshal.SizeOf(typeof(JOYINFOEX));
                info.dwFlags = 0xFF; // JOY_RETURNALL
                if (joyGetPosEx(id, ref info) != 0) continue;

                JOYCAPSW caps = new JOYCAPSW();
                joyGetDevCapsW(id, ref caps, Marshal.SizeOf(typeof(JOYCAPSW)));

                Device d = new Device();
                d.Id = id; d.Name = caps.szPname; d.Vid = caps.wMid; d.Pid = caps.wPid;
                d.NumAxes = caps.wNumAxes; d.NumButtons = caps.wNumButtons;
                d.Min = new[] { caps.wXmin, caps.wYmin, caps.wZmin, caps.wRmin, caps.wUmin, caps.wVmin };
                d.Max = new[] { caps.wXmax, caps.wYmax, caps.wZmax, caps.wRmax, caps.wUmax, caps.wVmax };
                d.Axes = new[] { info.dwXpos, info.dwYpos, info.dwZpos, info.dwRpos, info.dwUpos, info.dwVpos };
                d.Buttons = info.dwButtons;
                d.Pov = info.dwPOV;
                devices.Add(d);
            }
        }

        void TakeBaseline()
        {
            baseAxes.Clear();
            baseButtons.Clear();
            foreach (Device d in devices)
            {
                baseAxes[d.Id] = (int[])d.Axes.Clone();
                baseButtons[d.Id] = d.Buttons;
            }
            candidateDev = -1; candidateIdx = -1;
        }

        static float Frac(Device d, int i, int value)
        {
            int range = d.Max[i] - d.Min[i];
            return range <= 0 ? 0f : (float)(value - d.Min[i]) / range;
        }

        // ---------------- test logic ----------------
        void Restart()
        {
            results.Clear();
            stepIndex = 0;
            waitingForRelease = false;
            lastResult = "";
            restartTime = Environment.TickCount;
            Poll();
            TakeBaseline();
        }

        void SkipStep()
        {
            if (stepIndex >= steps.Length) return;
            Step s = steps[stepIndex];
            if (s.Key == "Center")
            {
                RecordCenter();
            }
            else
            {
                results.Add(s.Key + " = skipped");
                lastResult = s.Key + ": skipped";
            }
            NextStep();
        }

        void RecordCenter()
        {
            foreach (Device d in devices)
            {
                StringBuilder sb = new StringBuilder("Center (device " + d.Id + ") =");
                for (int i = 0; i < 6; i++) sb.Append(' ').Append(AxisNames[i]).Append(':').Append(d.Axes[i]);
                results.Add(sb.ToString());
            }
            lastResult = "Center position saved";
        }

        void NextStep()
        {
            stepIndex++;
            waitingForRelease = false;
            TakeBaseline();
            if (stepIndex >= steps.Length) SaveResults();
        }

        void Update()
        {
            if (stepIndex >= steps.Length) return;
            Step s = steps[stepIndex];
            int now = Environment.TickCount;

            // the driver reports default values for a moment after start: keep re-taking the baseline for 1.5 s
            if (now - restartTime < 1500)
            {
                TakeBaseline();
                return;
            }

            if (waitingForRelease)
            {
                bool released = true;
                foreach (Device d in devices)
                {
                    int[] b; int bb;
                    if (!baseAxes.TryGetValue(d.Id, out b) || !baseButtons.TryGetValue(d.Id, out bb)) continue;
                    if (d.Buttons != bb) released = false;
                    for (int i = 0; i < 6; i++)
                        if (Math.Abs(Frac(d, i, d.Axes[i]) - Frac(d, i, b[i])) > 0.15f) released = false;
                }
                if (released) NextStep();
                return;
            }

            if (s.Key == "Center") return; // finished with F10

            if (s.Kind == StepKind.Button)
            {
                foreach (Device d in devices)
                {
                    int bb;
                    if (!baseButtons.TryGetValue(d.Id, out bb)) continue;
                    int newly = d.Buttons & ~bb;
                    if (newly == 0) continue;
                    for (int b = 0; b < 32; b++)
                    {
                        if ((newly & (1 << b)) == 0) continue;
                        results.Add(string.Format("{0} = device {1} button {2}", s.Key, d.Id, b + 1));
                        lastResult = string.Format("{0}: button {1} on device {2}", s.Key, b + 1, d.Id);
                        waitingForRelease = true;
                        return;
                    }
                }
                return;
            }

            // Axis step: find the axis that moved the most from the baseline
            float best = 0f; int bestDev = -1, bestIdx = -1, bestVal = 0;
            foreach (Device d in devices)
            {
                int[] b;
                if (!baseAxes.TryGetValue(d.Id, out b)) continue;
                for (int i = 0; i < 6; i++)
                {
                    float diff = Math.Abs(Frac(d, i, d.Axes[i]) - Frac(d, i, b[i]));
                    if (diff > best) { best = diff; bestDev = d.Id; bestIdx = i; bestVal = d.Axes[i]; }
                }
            }

            if (best < AxisThreshold)
            {
                candidateDev = -1;
                return;
            }

            if (bestDev != candidateDev || bestIdx != candidateIdx)
            {
                candidateDev = bestDev; candidateIdx = bestIdx; candidateSince = now; candidatePeak = bestVal;
                return;
            }

            int rest = baseAxes[candidateDev][candidateIdx];
            if (Math.Abs(bestVal - rest) > Math.Abs(candidatePeak - rest))
                candidatePeak = bestVal;

            if (now - candidateSince >= HoldMs)
            {
                results.Add(string.Format("{0} = device {1} axis {2}  rest {3} -> pressed {4}",
                    s.Key, candidateDev, AxisNames[candidateIdx], rest, candidatePeak));
                lastResult = string.Format("{0}: axis {1} ({2} -> {3})", s.Key, AxisNames[candidateIdx], rest, candidatePeak);

                if (s.NeedsRelease) waitingForRelease = true;
                else NextStep();
            }
        }

        void SaveResults()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("WheelInputTest results - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine();
                foreach (Device d in devices)
                {
                    sb.AppendLine(string.Format("Device {0}: {1}  VID {2:X4} PID {3:X4}  axes {4}  buttons {5}",
                        d.Id, d.Name, d.Vid, d.Pid, d.NumAxes, d.NumButtons));
                    for (int i = 0; i < 6; i++)
                        sb.AppendLine(string.Format("   {0}: range {1}..{2}", AxisNames[i], d.Min[i], d.Max[i]));
                }
                sb.AppendLine();
                foreach (string r in results) sb.AppendLine(r);
                File.WriteAllText(resultsPath, sb.ToString());
                lastResult = "Saved to " + resultsPath;
                IVGame.Console.Print("WheelInputTest: results saved to " + resultsPath);
            }
            catch (Exception ex)
            {
                lastResult = "Could not save results: " + ex.Message;
                IVGame.Console.PrintError("WheelInputTest: " + ex.Message);
            }
        }

        // ---------------- drawing ----------------
        void Main_OnImGuiRendering(IntPtr devicePtr, ImGuiIV_DrawingContext ctx)
        {
            Poll();
            Update();

            if (!showWindow) return;

            windowOpen = true;
            ImGuiIV.SetNextWindowSize(new Vector2(460f, 0f), eImGuiCond.FirstUseEver);
            if (ImGuiIV.Begin("Wheel Input Test   F9 hide | F10 skip | F11 restart", ref windowOpen, eImGuiWindowFlags.AlwaysAutoResize))
            {
                if (stepIndex < steps.Length)
                {
                    ImGuiIV.TextUnformatted(string.Format("Step {0} of {1}:", stepIndex + 1, steps.Length));
                    ImGuiIV.TextUnformatted(waitingForRelease ? ">>> Got it - now let go." : ">>> " + steps[stepIndex].Prompt);
                }
                else
                {
                    ImGuiIV.TextUnformatted("Test finished - results saved. Tell Claude it's done.");
                    ImGuiIV.TextUnformatted("F11 to run it again.");
                }
                if (lastResult.Length > 0) ImGuiIV.TextUnformatted("Last: " + lastResult);

                ImGuiIV.Separator();

                if (devices.Count == 0)
                    ImGuiIV.TextUnformatted("No game controller found. Is the wheel plugged in and visible in joy.cpl?");

                foreach (Device d in devices)
                {
                    ImGuiIV.TextUnformatted(string.Format("Device {0}: {1}", d.Id, d.Name));
                    for (int i = 0; i < 6; i++)
                    {
                        float f = Frac(d, i, d.Axes[i]);
                        if (f < 0f) f = 0f;
                        if (f > 1f) f = 1f;
                        ImGuiIV.TextUnformatted(string.Format("{0} {1,6}", AxisNames[i], d.Axes[i]));
                        ImGuiIV.SameLine();
                        ImGuiIV.ProgressBar(f, new Vector2(240f, 0f), string.Format("{0:0}%", f * 100f));
                    }
                    StringBuilder pressed = new StringBuilder();
                    for (int b = 0; b < 32; b++)
                        if ((d.Buttons & (1 << b)) != 0) pressed.Append(b + 1).Append(' ');
                    ImGuiIV.TextUnformatted("Buttons: " + (pressed.Length > 0 ? pressed.ToString() : "-"));
                }
            }
            ImGuiIV.End();

            if (!windowOpen) showWindow = false;
        }
    }
}

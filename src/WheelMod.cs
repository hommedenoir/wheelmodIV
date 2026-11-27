// GTA IV Wheel Mod - milestone 3
// Steering, throttle and brake from any DirectInput wheel (default: Thrustmaster T248),
// per-car steering ratio, force feedback: tyre force (SAT), spring, damper, soft lock, bumps, crashes.
// Gearbox: paddles shift (Sequential) or pick D/R (Automatic). On foot nothing changes.
//
// Keys:  F6 = show/hide status window   F7 = wheel on/off
// Settings: WheelMod.ini next to this script (created automatically on first start).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

using IVSDKDotNet;
using IVSDKDotNet.Enums;
using IVSDKDotNet.Native;

namespace WheelMod
{
    // ------------------------------------------------------------------ input
    static class Joy
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct JOYINFOEX
        {
            public int dwSize, dwFlags;
            public int dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos;
            public int dwButtons, dwButtonNumber, dwPOV, dwReserved1, dwReserved2;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct JOYCAPSW
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

        [DllImport("winmm.dll")] public static extern int joyGetPosEx(int id, ref JOYINFOEX pji);
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] public static extern int joyGetDevCapsW(int id, ref JOYCAPSW pjc, int size);
        [DllImport("winmm.dll")] public static extern int joyGetNumDevs();

        public static readonly string[] AxisNames = { "X", "Y", "Z", "R", "U", "V" };

        public static int AxisIndex(string name)
        {
            return Array.IndexOf(AxisNames, (name ?? "").Trim().ToUpperInvariant());
        }
    }

    class WheelDevice
    {
        public int Id = -1;
        public string Name = "";
        public int[] Min = new int[6], Max = new int[6], Axes = new int[6];
        public int Buttons;
        public int Pov = -1;   // D-pad: hundredths of a degree, 0 = up; -1 or 65535 = not pressed

        // Find a device by VID/PID. Returns null if not connected.
        public static WheelDevice Find(ushort vid, ushort pid)
        {
            int count = Math.Min(Joy.joyGetNumDevs(), 16);
            for (int id = 0; id < count; id++)
            {
                Joy.JOYCAPSW caps = new Joy.JOYCAPSW();
                if (Joy.joyGetDevCapsW(id, ref caps, Marshal.SizeOf(typeof(Joy.JOYCAPSW))) != 0) continue;
                if (caps.wMid != vid || caps.wPid != pid) continue;

                WheelDevice d = new WheelDevice();
                d.Id = id;
                d.Name = caps.szPname;
                d.Min = new[] { caps.wXmin, caps.wYmin, caps.wZmin, caps.wRmin, caps.wUmin, caps.wVmin };
                d.Max = new[] { caps.wXmax, caps.wYmax, caps.wZmax, caps.wRmax, caps.wUmax, caps.wVmax };
                if (d.Poll()) return d;
            }
            return null;
        }

        public bool Poll()
        {
            Joy.JOYINFOEX info = new Joy.JOYINFOEX();
            info.dwSize = Marshal.SizeOf(typeof(Joy.JOYINFOEX));
            info.dwFlags = 0xFF; // JOY_RETURNALL
            if (Joy.joyGetPosEx(Id, ref info) != 0) return false;
            Axes[0] = info.dwXpos; Axes[1] = info.dwYpos; Axes[2] = info.dwZpos;
            Axes[3] = info.dwRpos; Axes[4] = info.dwUpos; Axes[5] = info.dwVpos;
            Buttons = info.dwButtons;
            Pov = info.dwPOV;
            return true;
        }

        // 0..1
        public float Axis01(int idx)
        {
            if (idx < 0 || idx > 5) return 0f;
            int range = Max[idx] - Min[idx];
            if (range <= 0) return 0f;
            float f = (float)(Axes[idx] - Min[idx]) / range;
            return f < 0f ? 0f : (f > 1f ? 1f : f);
        }

        public bool Button(int number) // 1-based, 0 = not mapped
        {
            if (number < 1 || number > 32) return false;
            return (Buttons & (1 << (number - 1))) != 0;
        }
    }

    // ------------------------------------------------------------------ settings
    class WheelSettings
    {
        public ushort Vid = 0x044F, Pid = 0xB696;     // Thrustmaster T248
        public int SteerAxis = 0, ThrottleAxis = 1, BrakeAxis = 2, ClutchAxis = 3;
        public bool InvertSteer = false, InvertThrottle = true, InvertBrake = true, InvertClutch = true;
        public float WheelRotationDeg = 900f; // what the wheel is set to in its driver
        public float SteeringRatio = 6f;      // wheel degrees per degree of front-wheel angle
        public float SteerQuickness = 1f;     // extra multiplier on top of the ratio
        public bool FfbEnabled = true;
        public float SpringMin = 0.05f, SpringMax = 0.1f, SpringFullSpeed = 30f;   // centering, 0..1, m/s
        public float DamperMax = 0.35f, DamperMin = 0.08f, DamperFadeSpeed = 15f;   // heavy when slow
        public float SoftLockStrength = 1f;
        public float SatGain = 0.8f;          // self-aligning torque from the front tyres (0..1)
        public float SatFullSpeed = 12f;      // m/s at which the tyre force is fully there
        public float SatPeakDeg = 0f;         // tyre slip angle of maximum grip, 0 = take it from the car's handling
        public float DetailGain = 0.4f;       // bumps / body movement
        public float CollisionGain = 0.8f;    // jolt when hitting something
        public float CollisionG = 3f;         // impacts above this many g count as a crash
        public bool AirMakesLight = true;     // steering goes light when the front wheels leave the ground
        public float FfbGain = 1f;
        public bool FfbInvert = false;
        public float SteerDeadzone = 0.0f;    // fraction of half-range
        public float PedalDeadzone = 0.03f;   // fraction of travel
        public float PedalSaturation = 0.97f; // pressed this far = 100 %
        // [ButtonMap]: input (lower case, e.g. button5, povup) -> action
        public Dictionary<string, string> ButtonMap = DefaultButtonMap();
        public bool ButtonSetupDone = false;
        public string HornKey = "G";
        public int HandbrakeButton = 0;       // 0 = leave handbrake to the game
        public int ShiftUpButton = 2;         // T248: right paddle
        public int ShiftDownButton = 1;       // T248: left paddle
        public string GearboxMode = "Sequential";   // Sequential or Automatic
        public bool ThrottleCut = true;       // lift the throttle briefly on upshifts
        public int ShiftTimeMs = 150;
        public bool AntiStall = false;        // shift down by itself when the gear is too high to pull
        public string ClutchMode = "Auto";    // Auto = mod does the clutch, the pedal overrides it. Manual = only the pedal
        public float EngineBraking = 0.15f;   // extra engine braking when off the throttle (0 = only GTA's own)
        public float ClutchInTime = 0.35f;    // seconds for the clutch to come back in after a shift
        public bool PatchGearbox = true;      // switch off GTA's own up/down shifting (Sequential mode)
        public bool ForceClutch = false;      // keep the game's clutch closed in gear (GTA opens it when revs are low)
        public bool TorqueFill = false;       // let a too-high gear still pull (weaker), like a real car
        public float TorqueFillRpm = 0.23f;   // below this (game rpm 0..1) the game itself gives (almost) no drive
        public int GearForceMode = 0;         // test: 0 = set gear only, 1 = gear + next gear, 2 = do not touch (watch only)
        public bool ShowHud = true;           // gear + rev counter on screen
        public float ShiftLightRpm = 0.9f;
        public float IdleRpm = 800f, RedlineRpm = 7000f;   // only for the number on the rev counter    // rev counter turns red from here (0..1)
        public bool TakeOverOnlyWhenMoved = true;
        public float ReverseSpeed = 1.0f;     // m/s below which brake = reverse

        string path;

        public static WheelSettings Load(string iniPath)
        {
            WheelSettings s = new WheelSettings();
            s.path = iniPath;
            if (!File.Exists(iniPath)) { s.Save(); return s; }

            foreach (string raw in File.ReadAllLines(iniPath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#") || line.StartsWith("[")) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                string v = line.Substring(eq + 1).Trim();
                int sc = v.IndexOf(';'); if (sc >= 0) v = v.Substring(0, sc).Trim();
                try { s.Apply(k, v); } catch { }
            }
            s.Save(); // adds any new settings, keeps your values
            return s;
        }

        static Dictionary<string, string> DefaultButtonMap()
        {
            Dictionary<string, string> m = new Dictionary<string, string>();
            m["povup"] = "Hazards";
            m["povright"] = "IndicatorRight";
            m["povdown"] = "Headlights";
            m["povleft"] = "IndicatorLeft";
            for (int b = 3; b <= 26; b++) m["button" + b] = "none";
            return m;
        }

        static bool B(string v) { v = v.ToLowerInvariant(); return v == "1" || v == "true" || v == "yes" || v == "on"; }
        static float F(string v) { return float.Parse(v, CultureInfo.InvariantCulture); }
        static ushort Hex(string v) { return Convert.ToUInt16(v.Replace("0x", ""), 16); }

        void Apply(string k, string v)
        {
            if (k.StartsWith("pov") || (k.StartsWith("button") && k.Length > 6 && char.IsDigit(k[6])))
            {
                ButtonMap[k] = v.Length == 0 ? "none" : v;
                return;
            }
            if (k == "buttonsetupdone") { ButtonSetupDone = B(v); return; }
            if (k == "hornkey") { HornKey = v; return; }
            switch (k)
            {
                case "vid": Vid = Hex(v); break;
                case "pid": Pid = Hex(v); break;
                case "steeraxis": SteerAxis = Joy.AxisIndex(v); break;
                case "throttleaxis": ThrottleAxis = Joy.AxisIndex(v); break;
                case "brakeaxis": BrakeAxis = Joy.AxisIndex(v); break;
                case "clutchaxis": ClutchAxis = Joy.AxisIndex(v); break;
                case "invertsteer": InvertSteer = B(v); break;
                case "invertthrottle": InvertThrottle = B(v); break;
                case "invertbrake": InvertBrake = B(v); break;
                case "invertclutch": InvertClutch = B(v); break;
                case "wheelrotationdeg": WheelRotationDeg = F(v); break;
                case "steeringratio": SteeringRatio = F(v); break;
                case "steerquickness": SteerQuickness = F(v); break;
                case "ffbenabled": FfbEnabled = B(v); break;
                case "springmin": SpringMin = F(v); break;
                case "springmax": SpringMax = F(v); break;
                case "springfullspeed": SpringFullSpeed = F(v); break;
                case "dampermax": DamperMax = F(v); break;
                case "dampermin": DamperMin = F(v); break;
                case "damperfadespeed": DamperFadeSpeed = F(v); break;
                case "softlockstrength": SoftLockStrength = F(v); break;
                case "ffbgain": FfbGain = F(v); break;
                case "satgain": SatGain = F(v); break;
                case "satfullspeed": SatFullSpeed = F(v); break;
                case "satpeakdeg": SatPeakDeg = F(v); break;
                case "detailgain": DetailGain = F(v); break;
                case "collisiongain": CollisionGain = F(v); break;
                case "collisiong": CollisionG = F(v); break;
                case "airmakeslight": AirMakesLight = B(v); break;
                case "ffbinvert": FfbInvert = B(v); break;
                case "steerdeadzone": SteerDeadzone = F(v); break;
                case "pedaldeadzone": PedalDeadzone = F(v); break;
                case "pedalsaturation": PedalSaturation = F(v); break;
                case "handbrakebutton": HandbrakeButton = int.Parse(v); break;
                case "shiftupbutton": ShiftUpButton = int.Parse(v); break;
                case "shiftdownbutton": ShiftDownButton = int.Parse(v); break;
                case "gearboxmode": GearboxMode = v.ToLowerInvariant().StartsWith("a") ? "Automatic" : "Sequential"; break;
                case "throttlecut": ThrottleCut = B(v); break;
                case "shifttimems": ShiftTimeMs = int.Parse(v); break;
                case "showhud": ShowHud = B(v); break;
                case "gearforcemode": GearForceMode = int.Parse(v); break;
                case "antistall": AntiStall = B(v); break;
                case "torquefill": TorqueFill = B(v); break;
                case "forceclutch": ForceClutch = B(v); break;
                case "patchgearbox": PatchGearbox = B(v); break;
                case "clutchintime": ClutchInTime = F(v); break;
                case "clutchmode": ClutchMode = v.ToLowerInvariant().StartsWith("m") ? "Manual" : "Auto"; break;
                case "enginebraking": EngineBraking = F(v); break;
                case "torquefillrpm": TorqueFillRpm = F(v); break;
                case "shiftlightrpm": ShiftLightRpm = F(v); break;
                case "idlerpm": IdleRpm = F(v); break;
                case "redlinerpm": RedlineRpm = F(v); break;
                case "takeoveronlywhenmoved": TakeOverOnlyWhenMoved = B(v); break;
                case "reversespeed": ReverseSpeed = F(v); break;
            }
        }

        static string A(int i) { return i >= 0 && i < 6 ? Joy.AxisNames[i] : "none"; }
        static string Fs(float f) { return f.ToString("0.###", CultureInfo.InvariantCulture); }

        public void Save()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; GTA IV Wheel Mod settings. Axes: X Y Z R U V (or none). Buttons: 1-32 (0 = none).");
            sb.AppendLine("[Device]");
            sb.AppendLine("VID=" + Vid.ToString("X4") + "   ; 044F = Thrustmaster");
            sb.AppendLine("PID=" + Pid.ToString("X4") + "   ; B696 = T248");
            sb.AppendLine();
            sb.AppendLine("[Axes]");
            sb.AppendLine("SteerAxis=" + A(SteerAxis));
            sb.AppendLine("ThrottleAxis=" + A(ThrottleAxis));
            sb.AppendLine("BrakeAxis=" + A(BrakeAxis));
            sb.AppendLine("ClutchAxis=" + A(ClutchAxis) + "   ; clutch pedal");
            sb.AppendLine("InvertSteer=" + InvertSteer);
            sb.AppendLine("InvertThrottle=" + InvertThrottle + "   ; T248 pedals report 65535 when released");
            sb.AppendLine("InvertBrake=" + InvertBrake);
            sb.AppendLine("InvertClutch=" + InvertClutch);
            sb.AppendLine();
            sb.AppendLine("[Tuning]");
            sb.AppendLine("WheelRotationDeg=" + Fs(WheelRotationDeg) + "   ; set this to what the wheel is set to in its driver (T248: 900)");
            sb.AppendLine("SteeringRatio=" + Fs(SteeringRatio) + "   ; wheel degrees per degree of front-wheel angle. Lower = quicker steering");
            sb.AppendLine("SteerQuickness=" + Fs(SteerQuickness) + "   ; extra multiplier, 1 = as the ratio says, 2 = twice as quick");
            sb.AppendLine("SteerDeadzone=" + Fs(SteerDeadzone));
            sb.AppendLine("PedalDeadzone=" + Fs(PedalDeadzone));
            sb.AppendLine("PedalSaturation=" + Fs(PedalSaturation));
            sb.AppendLine("ReverseSpeed=" + Fs(ReverseSpeed) + "   ; m/s: below this you can shift into R (and out of it)");
            sb.AppendLine();
            sb.AppendLine("[ForceFeedback]");
            sb.AppendLine("FfbEnabled=" + FfbEnabled);
            sb.AppendLine("FfbGain=" + Fs(FfbGain) + "   ; overall strength of everything below");
            sb.AppendLine("FfbInvert=" + FfbInvert + "   ; set to True if the wheel pushes AWAY from the centre instead of towards it");
            sb.AppendLine("SpringMin=" + Fs(SpringMin) + "   ; centering force when standing still (0..1)");
            sb.AppendLine("SpringMax=" + Fs(SpringMax) + "   ; extra fixed centering at speed (0..1); the tyre force does most of the work now");
            sb.AppendLine("SpringFullSpeed=" + Fs(SpringFullSpeed) + "   ; m/s at which SpringMax is reached");
            sb.AppendLine("DamperMax=" + Fs(DamperMax) + "   ; heaviness when standing still (0..1)");
            sb.AppendLine("DamperMin=" + Fs(DamperMin) + "   ; heaviness at speed (0..1)");
            sb.AppendLine("DamperFadeSpeed=" + Fs(DamperFadeSpeed) + "   ; m/s at which DamperMin is reached");
            sb.AppendLine("SoftLockStrength=" + Fs(SoftLockStrength) + "   ; resistance at the car's steering lock (0..1)");
            sb.AppendLine("SatGain=" + Fs(SatGain) + "   ; tyre force: weight that builds in corners and goes light when the front slides (0..1)");
            sb.AppendLine("SatFullSpeed=" + Fs(SatFullSpeed) + "   ; m/s at which the tyre force is fully there");
            sb.AppendLine("SatPeakDeg=" + Fs(SatPeakDeg) + "   ; slip angle where the front tyres grip most, 0 = from the car's handling");
            sb.AppendLine("DetailGain=" + Fs(DetailGain) + "   ; bumps and body movement (0..1)");
            sb.AppendLine("CollisionGain=" + Fs(CollisionGain) + "   ; jolt when you hit something (0..1)");
            sb.AppendLine("CollisionG=" + Fs(CollisionG) + "   ; how hard an impact must be to count, in g");
            sb.AppendLine("AirMakesLight=" + AirMakesLight + "   ; steering goes light when the front wheels leave the ground");
            sb.AppendLine();
            sb.AppendLine("[Buttons]");
            sb.AppendLine("HandbrakeButton=" + HandbrakeButton);
            sb.AppendLine("ShiftUpButton=" + ShiftUpButton + "   ; T248 right paddle = 2");
            sb.AppendLine("ShiftDownButton=" + ShiftDownButton + "   ; T248 left paddle = 1");
            sb.AppendLine();
            sb.AppendLine("[ButtonMap]");
            sb.AppendLine("; Easiest: press F8 in a car for the button setup dialog. Or edit here.");
            sb.AppendLine("; Actions: none, Horn, Hazards, IndicatorLeft, IndicatorRight, Headlights (auto/on/off), Siren, InteriorLight, WheelOnOff,");
            sb.AppendLine(";          Key:<key> = hold that keyboard key, e.g. Key:Up (phone), Key:V (camera), Key:F (get out)");
            sb.AppendLine("; Buttons 1 and 2 are the T248 paddles, already used for shifting. F6 window shows the last button pressed.");
            sb.AppendLine("HornKey=" + HornKey + "   ; the game's horn key, used by the Horn action (check GTA IV Settings > Controls)");
            sb.AppendLine("ButtonSetupDone=" + ButtonSetupDone + "   ; False = the setup dialog opens the next time you sit in a car");
            foreach (string pn in ButtonMapper.PovNames)
            {
                string val; ButtonMap.TryGetValue(pn.ToLowerInvariant(), out val);
                sb.AppendLine(pn + "=" + (val ?? "none"));
            }
            List<int> nums = new List<int>();
            foreach (string key in ButtonMap.Keys)
            {
                int n;
                if (key.StartsWith("button") && int.TryParse(key.Substring(6), out n)) nums.Add(n);
            }
            nums.Sort();
            foreach (int n in nums) sb.AppendLine("Button" + n + "=" + ButtonMap["button" + n]);
            sb.AppendLine();
            sb.AppendLine("[Gearbox]");
            sb.AppendLine("GearboxMode=" + GearboxMode + "   ; Sequential = you shift with the paddles (R-N-1-2-...). Automatic = game shifts, paddles pick R-N-D");
            sb.AppendLine("ThrottleCut=" + ThrottleCut + "   ; lift the throttle briefly on upshifts");
            sb.AppendLine("ShiftTimeMs=" + ShiftTimeMs + "   ; how long a shift takes");
sb.AppendLine("ClutchMode=" + ClutchMode + "   ; Auto = the mod works the clutch, pressing the pedal overrides it. Manual = only your pedal (released = engaged)");
            sb.AppendLine("EngineBraking=" + Fs(EngineBraking) + "   ; extra engine braking off the throttle, stronger in low gears and at high revs (0 = off)");
            sb.AppendLine("ClutchInTime=" + Fs(ClutchInTime) + "   ; seconds for the clutch to come back in after a shift");
            sb.AppendLine("PatchGearbox=" + PatchGearbox + "   ; switch off GTA's own automatic shifting while in Sequential mode (the real fix)");
            sb.AppendLine("ForceClutch=" + ForceClutch + "   ; keep the clutch closed in gear, so a high gear at low speed still pulls (GTA opens it by itself)");
            sb.AppendLine("TorqueFill=" + TorqueFill + "   ; a high gear at low speed still pulls (weaker), like a real road car. GTA alone gives no power there");
            sb.AppendLine("TorqueFillRpm=" + Fs(TorqueFillRpm) + "   ; game rpm (0..1) below which TorqueFill helps");
            sb.AppendLine("AntiStall=" + AntiStall + "   ; instead: shift down by itself when the gear is too high to pull");
            sb.AppendLine("GearForceMode=" + GearForceMode + "   ; TEST: 0 = set gear only, 1 = gear + next gear, 2 = don't touch the game's gearbox");
            sb.AppendLine("ShowHud=" + ShowHud + "   ; gear, rev counter and speed in the bottom right corner");
sb.AppendLine("IdleRpm=" + Fs(IdleRpm) + "   ; rev counter number at idle");
            sb.AppendLine("RedlineRpm=" + Fs(RedlineRpm) + "   ; rev counter number at the limiter");
            sb.AppendLine("ShiftLightRpm=" + Fs(ShiftLightRpm) + "   ; rev counter turns red (shift!) from here, 0..1");
            sb.AppendLine();
            sb.AppendLine("[Behaviour]");
            sb.AppendLine("TakeOverOnlyWhenMoved=" + TakeOverOnlyWhenMoved + "   ; wheel takes over the car once you move it");
            try { File.WriteAllText(path, sb.ToString()); } catch { }
        }
    }

    // ------------------------------------------------------------------ script
    public class Main : Script
    {
        WheelSettings cfg;
        string iniPath;
        WheelDevice wheel;
        int nextDeviceSearch = 0;

        bool enabled = true;
        bool showWindow = true;
        bool windowOpen = true;

        // state shared between Tick (script thread) and ProcessAutomobile (game)
        volatile bool playerDriving = false;
        float carSpeed = 0f;
        bool wheelTookOver = false;
        float takeoverRefSteer = -1f;
        bool reversing = false;
        // gearbox: -1 = R, 0 = N, 1.. = forward gears (Automatic mode: 1 = D)
        int selGear = 1, maxGears = 5, shiftUntil = 0;
        bool upWas = false, downWas = false;
        int lugSince = 0, driveGear = 1, lowRevSince = 0;
        bool justShifted = false;
        float modClutch = 1f; int lastClutchMs = 0;
        float inClutch = 0f, clutchEngaged = 1f;
        float[] gearSpeeds = new float[8];

        // top speed of each gear from the car's handling (index 1..7)
        float[] GearSpeeds(IVVehicle veh)
        {
            try
            {
                IVHandlingData h = veh.Handling;
                if (h != null)
                {
                    gearSpeeds[1] = h.V_gear1; gearSpeeds[2] = h.V_gear2; gearSpeeds[3] = h.V_gear3; gearSpeeds[4] = h.V_gear4;
                    gearSpeeds[5] = h.V_gear5; gearSpeeds[6] = h.V_gear6; gearSpeeds[7] = h.V_gear7;
                }
            }
            catch { }
            return gearSpeeds;
        }
        int gameGear = 0, gameNextGear = 0;
        float engineRpm = 0f;
        string gearDebug = "";

        // last values written, for the status window
        float outSteer, outGas, outBrake, inSteer, inThrottle, inBrake;
        float steeringLock, carLockDeg, lockWheelDeg = 450f;
        ForceFeedback ffb = new ForceFeedback();
        float lastX, velFiltered, lastForce;

        // car motion, measured in Tick (car frame: long = forwards, lat = to the right)
        int carHandle = 0, lastCarTime = 0;
        bool carValid = false;
        float carLong, carLat, yawRight, rollRate, rollRateSmooth, lastHeading, lastRoll, lastLong, lastLat, impactG;
        uint lastHealth;
        // from ProcessAutomobile (game thread)
        volatile float frontGrounded = 1f;
        volatile int frontWheels = 0, frontOnGround = 0;
        bool seenGrounded = false;
        float tractionPeakRaw = 0f;
        // force parts, for the status window
        float fSpring, fSat, fDamper, fLock, fDetail, fCrash, slipDeg, peakDeg;
        float crashForce = 0f; int crashUntil = 0;
        int lastFfbTime = 0;
        int nextFfbInit = 0;

        public Main()
        {
            Initialized += Main_Initialized;
            Tick += Main_Tick;
            KeyDown += Main_KeyDown;
            ProcessAutomobile += Main_ProcessAutomobile;
            OnImGuiRendering += Main_OnImGuiRendering;
            Uninitialize += (s, e) => { buttons.ReleaseAll(); ffb.Dispose(); GearboxPatch.Restore(); };
        }

        void Main_Initialized(object sender, EventArgs e)
        {
            iniPath = Path.Combine(Path.GetDirectoryName(GetFullPath()), "WheelMod.ini");
            cfg = WheelSettings.Load(iniPath);
            RegisterConsoleCommand("wheelmod_reload", args =>
            {
                cfg = WheelSettings.Load(iniPath);
                wheel = null;
                ffb.Dispose();
                nextFfbInit = 0;
                IVGame.Console.Print("WheelMod: settings reloaded.");
            });
            RegisterConsoleCommand("wheelmod_blink", args =>
            {
                int m;
                if (args != null && args.Length > 0 && int.TryParse(args[0], out m) && m >= 0 && m <= 15)
                {
                    blinkTestMask = m;
                    IVGame.Console.Print("WheelMod: blink test " + m + " (1 left, 2 right, 4 hazard, 8 indicator). 'wheelmod_blink off' to stop.");
                }
                else { blinkTestMask = -1; IVGame.Console.Print("WheelMod: blink test off."); }
            });
            RegisterConsoleCommand("wheelmod_ffbtest", args =>
            {
                int m = 6000;
                if (args != null && args.Length > 0) int.TryParse(args[0], out m);
                IVGame.Console.Print("WheelMod: " + ffb.Kick(m));
            });
            IVGame.Console.Print("WheelMod loaded. F6 = status window, F7 = wheel on/off, F8 = button setup. Settings: " + iniPath);
        }

        void Main_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F6) showWindow = !showWindow;
            else if (e.KeyCode == Keys.F8) { if (wizard.Active) wizard.Close(); else wizard.Start(); }
            else if (e.KeyCode == Keys.F7)
            {
                enabled = !enabled;
                IVGame.ShowSubtitleMessage(enabled ? "Wheel ON" : "Wheel OFF");
            }
        }

        // Script thread: find out if the player is driving and how fast
        void Main_Tick(object sender, EventArgs e)
        {
            if (wheel == null && Environment.TickCount >= nextDeviceSearch)
            {
                wheel = WheelDevice.Find(cfg.Vid, cfg.Pid);
                nextDeviceSearch = Environment.TickCount + 3000;
            }

            int ped;
            Natives.GET_PLAYER_CHAR(Natives.CONVERT_INT_TO_PLAYERINDEX(Natives.GET_PLAYER_ID()), out ped);

            bool inCar = ped != 0 && Natives.IS_CHAR_IN_ANY_CAR(ped);
            if (inCar)
            {
                int veh;
                Natives.GET_CAR_CHAR_IS_USING(ped, out veh);
                float speed;
                Natives.GET_CAR_SPEED(veh, out speed);
                carSpeed = speed;
                ReadCarMotion(veh);
            }
            else
            {
                carSpeed = 0f;
                carValid = false; carHandle = 0;
            }

            if (!inCar && playerDriving)
            {
                // left the car: next time the wheel has to be moved again to take over
                wheelTookOver = false;
                takeoverRefSteer = -1f;
                reversing = false; selGear = 1;
            }
            playerDriving = inCar;

            // the game can take control away while the player still sits in the car:
            // death, arrest, cutscenes, mission scripts. Then the wheel must let go,
            // otherwise the mod keeps driving the car past the game (build 3p fix)
            try
            {
                int pl = Natives.CONVERT_INT_TO_PLAYERINDEX(Natives.GET_PLAYER_ID());
                controlOn = Natives.IS_PLAYER_PLAYING(pl)
                    && !Natives.IS_PLAYER_DEAD(pl)
                    && !(ped != 0 && Natives.IS_CHAR_DEAD(ped))
                    && Natives.IS_PLAYER_CONTROL_ON(pl);
            }
            catch { controlOn = true; }

            // the on-screen display steps aside whenever the game is busy with something else:
            // save prompts, phone dialogs, cutscenes (player control off), pause menu, fades,
            // or when the player has switched the HUD off in the game's settings
            try
            {
                int player = Natives.CONVERT_INT_TO_PLAYERINDEX(Natives.GET_PLAYER_ID());
                hudAllowed = Natives.IS_PLAYER_CONTROL_ON(player)
                    && !Natives.IS_PAUSE_MENU_ACTIVE()
                    && !Natives.IS_SCREEN_FADED_OUT()
                    && Natives.IS_HUD_PREFERENCE_SWITCHED_ON();
            }
            catch { hudAllowed = true; }

            UpdateFfb();
            UpdateButtons();
        }

        // Wheel buttons and D-pad -> actions from [ButtonMap], and the setup dialog
        void UpdateButtons()
        {
            if (wheel == null) return;
            if (!enabled || !playerDriving || !controlOn)
            {
                buttons.ReleaseAll();
                return;
            }
            if (!wheel.Poll()) return;
            if (carHandle != buttonsCar) { buttons.NewCar(); buttonsCar = carHandle; }
            buttons.Track(wheel);

            // save first: a finished or closed setup must count as done before anything can reopen it
            if (wizard.Finished)
            {
                wizard.Finished = false;
                cfg.ButtonSetupDone = true;
                cfg.Save();
                IVGame.ShowSubtitleMessage("Wheel buttons saved");
                return;
            }
            if (!cfg.ButtonSetupDone && !wizard.Active && carSpeed < 1f) wizard.Start();
            if (wizard.Active)
            {
                buttons.ReleaseAll();
                wizard.Update(wheel, cfg.ButtonMap, cfg.ShiftUpButton, cfg.ShiftDownButton);
                return;
            }

            try { buttons.HornKey = (Keys)Enum.Parse(typeof(Keys), cfg.HornKey, true); } catch { buttons.HornKey = Keys.G; }
            buttons.Update(wheel, cfg.ButtonMap, carHandle);
            if (buttons.WheelToggleRequested)
            {
                buttons.WheelToggleRequested = false;
                enabled = !enabled;
                IVGame.ShowSubtitleMessage(enabled ? "Wheel ON" : "Wheel OFF");
            }
        }

        static float Clamp1(float v) { return v > 1f ? 1f : (v < -1f ? -1f : v); }

        // Velocity, rotation and impacts of the player's car, in the car's own frame
        void ReadCarMotion(int veh)
        {
            Vector3 v, fwd;
            float heading, roll;
            uint health;
            Natives.GET_CAR_SPEED_VECTOR(veh, out v, false);
            Natives.GET_CAR_FORWARD_VECTOR(veh, out fwd);
            Natives.GET_CAR_HEADING(veh, out heading);
            Natives.GET_CAR_ROLL(veh, out roll);
            Natives.GET_CAR_HEALTH(veh, out health);

            float fl = (float)Math.Sqrt(fwd.X * fwd.X + fwd.Y * fwd.Y);
            if (fl < 0.01f) fl = 1f;
            float fx = fwd.X / fl, fy = fwd.Y / fl;
            float lng = v.X * fx + v.Y * fy;           // forwards
            float lat = v.X * fy - v.Y * fx;           // to the right (right = forward x up)

            int now = Environment.TickCount;
            float dt = (now - lastCarTime) / 1000f;
            bool fresh = veh != carHandle || !carValid || dt <= 0.001f || dt > 0.25f;
            if (veh != carHandle) { seenGrounded = false; }
            if (!fresh)
            {
                float dh = heading - lastHeading;
                if (dh > 180f) dh -= 360f;
                if (dh < -180f) dh += 360f;
                float yr = -dh * (float)Math.PI / 180f / dt;           // heading grows to the left
                yawRight = yawRight * 0.5f + yr * 0.5f;

                float dr = roll - lastRoll;
                if (dr > 180f) dr -= 360f;
                if (dr < -180f) dr += 360f;
                rollRate = rollRate * 0.3f + (dr / dt) * 0.7f;
                rollRateSmooth = rollRateSmooth * 0.9f + rollRate * 0.1f;

                float aLong = (lng - lastLong) / dt, aLat = (lat - lastLat) / dt;
                impactG = (float)Math.Sqrt(aLong * aLong + aLat * aLat) / 9.81f;
                bool hit = impactG > cfg.CollisionG || (lastHealth > health && lastHealth - health >= 15);
                if (hit && now > crashUntil - 100)
                {
                    float strength = Math.Min(1f, 0.4f + Math.Max(0f, impactG - cfg.CollisionG) / 6f);
                    float dir = Math.Abs(aLat) > 2f ? -Math.Sign(aLat) : ((now & 1) == 0 ? 1f : -1f);
                    crashForce = dir * strength;
                    crashUntil = now + 250;
                }
            }
            else
            {
                yawRight = 0f; rollRate = 0f; rollRateSmooth = 0f; impactG = 0f;
            }
            carLong = lng; carLat = lat;
            lastLong = lng; lastLat = lat; lastHeading = heading; lastRoll = roll; lastHealth = health;
            lastCarTime = now; carHandle = veh; carValid = true;
        }

        // Tyre force curve: rises to the grip peak (u = 1), then falls off as the tyre slides
        static float SatCurve(float u)
        {
            float a = Math.Abs(u), m;
            if (a <= 1f) m = (float)Math.Sin(a * Math.PI / 2.0);
            else m = Math.Max(0.25f, 1f - (a - 1f) * 0.75f);
            return Math.Sign(u) * m;
        }

        void UpdateFfb()
        {
            if (!cfg.FfbEnabled || wheel == null)
            {
                if (ffb.Ready) ffb.Dispose();
                return;
            }
            if (!ffb.Ready)
            {
                if (Environment.TickCount < nextFfbInit) return;
                nextFfbInit = Environment.TickCount + 5000;
                if (ffb.Init(cfg.Vid, cfg.Pid)) IVGame.Console.Print("WheelMod: force feedback running.");
                else IVGame.Console.PrintWarning("WheelMod: force feedback not started: " + ffb.Status);
                return;
            }

            if (!enabled || !playerDriving || !controlOn || !wheelTookOver)
            {
                ffb.Relax();
                return;
            }

            if (!wheel.Poll()) return;
            int now = Environment.TickCount;
            // wheel position in the device's own direction: -1 .. +1
            float x = wheel.Axis01(Math.Max(0, cfg.SteerAxis)) * 2f - 1f;
            float dt = Math.Max(0.001f, (now - lastFfbTime) / 1000f);
            if (lastFfbTime == 0 || dt > 0.25f) { lastX = x; dt = 0.016f; }
            float vel = (x - lastX) / dt;                  // half-ranges per second
            velFiltered = velFiltered * 0.6f + vel * 0.4f;
            lastX = x; lastFfbTime = now;

            float speed = Math.Abs(carSpeed);
            float spring = cfg.SpringMin + (cfg.SpringMax - cfg.SpringMin) * Math.Min(1f, speed / Math.Max(1f, cfg.SpringFullSpeed));
            float damper = cfg.DamperMax + (cfg.DamperMin - cfg.DamperMax) * Math.Min(1f, speed / Math.Max(1f, cfg.DamperFadeSpeed));
            float half = Math.Max(90f, cfg.WheelRotationDeg / 2f);
            float lockFrac = Math.Min(1f, lockWheelDeg / half);

            // fixed centering: full spring strength is reached at the car's full lock
            fSpring = -spring * Clamp1(x / lockFrac);
            // damping: resists fast wheel movement
            fDamper = -damper * Clamp1(velFiltered * 0.5f);
            // soft lock: a hard wall just past the car's full lock
            float over = Math.Abs(x) - lockFrac;
            fLock = over > 0f ? -Math.Sign(x) * Math.Min(1f, over * 25f) * cfg.SoftLockStrength : 0f;

            // tyre force (self-aligning torque): pulls the front wheels towards where the front axle
            // is actually travelling. Builds up in corners, goes light when the front tyres slide.
            fSat = 0f; fDetail = 0f;
            if (carValid)
            {
                float peakRad = cfg.SatPeakDeg > 0f ? cfg.SatPeakDeg * (float)Math.PI / 180f
                    : (tractionPeakRaw > 1.5f ? tractionPeakRaw * (float)Math.PI / 180f : tractionPeakRaw);
                if (peakRad < 0.1f || peakRad > 0.6f) peakRad = 0.3f;
                peakDeg = peakRad * 180f / (float)Math.PI;
                float latFront = carLat + yawRight * 1.3f;                  // front axle ~1.3 m ahead
                float travel = (float)Math.Atan2(latFront, Math.Max(2f, Math.Abs(carLong)));
                float wheelAngle = -outSteer;                               // radians, right = positive
                float slip = travel - wheelAngle;
                slipDeg = slip * 180f / (float)Math.PI;
                float fade = Math.Max(0f, Math.Min(1f, (carLong - 1f) / Math.Max(1f, cfg.SatFullSpeed - 1f)));
                float grip = 1f;
                if (cfg.AirMakesLight && seenGrounded && frontWheels > 0) grip = frontGrounded;
                fSat = cfg.SatGain * SatCurve(slip / peakRad) * fade * grip;

                // bumps and body movement: quick changes of the body roll
                float hp = rollRate - rollRateSmooth;
                fDetail = -cfg.DetailGain * Clamp1(hp / 40f) * Math.Min(1f, speed / 5f);
            }
            fCrash = now < crashUntil ? crashForce * cfg.CollisionGain * (crashUntil - now) / 250f : 0f;

            float force = fSpring + fDamper + fLock + fSat + fDetail + fCrash;

            force *= cfg.FfbGain;
            if (cfg.FfbInvert) force = -force;
            lastForce = Math.Max(-1f, Math.Min(1f, force));
            ffb.SetForce(lastForce);
        }

        float Pedal(int axis, bool invert)
        {
            if (axis < 0) return 0f;
            float f = wheel.Axis01(axis);
            if (invert) f = 1f - f;
            if (f <= cfg.PedalDeadzone) return 0f;
            f = (f - cfg.PedalDeadzone) / Math.Max(0.01f, cfg.PedalSaturation - cfg.PedalDeadzone);
            return f > 1f ? 1f : f;
        }

        float Steer()
        {
            if (cfg.SteerAxis < 0) return 0f;
            float s = wheel.Axis01(cfg.SteerAxis) * 2f - 1f; // -1 left .. +1 right
            if (cfg.InvertSteer) s = -s;
            float a = Math.Abs(s);
            if (a <= cfg.SteerDeadzone) return 0f;
            s = Math.Sign(s) * (a - cfg.SteerDeadzone) / (1f - cfg.SteerDeadzone);
            return s < -1f ? -1f : (s > 1f ? 1f : s);
        }

        // Game: called for every car each frame; we only touch the player's car
        void Main_ProcessAutomobile(UIntPtr vehPtr)
        {
            if (!enabled || !playerDriving || !controlOn || wheel == null) return;

            UIntPtr playerPtr = IVPlayerInfo.FindThePlayerPed();
            if (playerPtr == UIntPtr.Zero) return;
            IVPed player = IVPed.FromUIntPtr(playerPtr);
            if (player == null || player.GetVehicle() != vehPtr) return;

            IVVehicle veh = IVVehicle.FromUIntPtr(vehPtr);
            if (veh == null || veh.Driver != playerPtr) return;

            // indicators and hazards from the wheel buttons; written every frame so the game keeps them
            try
            {
                int ind = buttons.Indicator;
                bool haz = buttons.Hazards;
                int mask = blinkTestMask;
                if (mask >= 0)
                {
                    // test: wheelmod_blink <number>  1 = left flag, 2 = right flag, 4 = hazard flag, 8 = indicator flag
                    veh.VehicleFlags3.LeftIndicator = (mask & 1) != 0;
                    veh.VehicleFlags3.RightIndicator = (mask & 2) != 0;
                    veh.VehicleFlags3.HazardLights = (mask & 4) != 0;
                    veh.VehicleFlags3.IndicatorLights = (mask & 8) != 0;
                }
                else if (ind != 0 || haz || indicatorWasSet)
                {
                    // the 'indicator lights' flag blinks all four, so only the side flags are used.
                    // The side flags alone light steadily, so the mod blinks them itself (0.4 s on, 0.4 s off)
                    if (ind != lastIndicator) { blinkStart = Environment.TickCount; lastIndicator = ind; }
                    bool lit = ((Environment.TickCount - blinkStart) / 400) % 2 == 0;
                    veh.VehicleFlags3.LeftIndicator = !haz && ind == -1 && lit;
                    veh.VehicleFlags3.RightIndicator = !haz && ind == 1 && lit;
                    veh.VehicleFlags3.IndicatorLights = false;
                    veh.VehicleFlags3.HazardLights = haz;
                    indicatorWasSet = ind != 0 || haz;
                }
            }
            catch { }

            if (!wheel.Poll()) { wheel = null; return; }

            inSteer = Steer();
            inThrottle = Pedal(cfg.ThrottleAxis, cfg.InvertThrottle);
            inBrake = Pedal(cfg.BrakeAxis, cfg.InvertBrake);
            inClutch = cfg.ClutchAxis >= 0 ? Pedal(cfg.ClutchAxis, cfg.InvertClutch) : 0f;

            // Only take over once the wheel is actually used, so the controller still works otherwise
            if (!wheelTookOver)
            {
                if (!cfg.TakeOverOnlyWhenMoved) wheelTookOver = true;
                else
                {
                    if (takeoverRefSteer < -0.5f) takeoverRefSteer = inSteer;
                    if (Math.Abs(inSteer - takeoverRefSteer) > 0.05f || inThrottle > 0.1f || inBrake > 0.1f)
                        wheelTookOver = true;
                }
                if (!wheelTookOver) return;
            }

            // ---- gearbox: paddles shift, R lies below 1st and only engages when (nearly) stopped
            bool sequential = cfg.GearboxMode != "Automatic";
            if (sequential && cfg.PatchGearbox) GearboxPatch.Apply();
            else if (GearboxPatch.Active) GearboxPatch.Restore();
            bool patched = GearboxPatch.Active;
            try { if (veh.Handling != null) maxGears = (int)Math.Max(1u, Math.Min(7u, veh.Handling.DriveGears)); } catch { }
            if (!sequential) maxGears = 1;
            if (selGear > maxGears) selGear = maxGears;
            bool up = !wizard.Active && cfg.ShiftUpButton > 0 && wheel.Button(cfg.ShiftUpButton);
            bool down = !wizard.Active && cfg.ShiftDownButton > 0 && wheel.Button(cfg.ShiftDownButton);
            int nowMs = Environment.TickCount;
            // order: R - N - 1 - 2 ... ; R <-> N only when (nearly) stopped
            if (up && !upWas)
            {
                if (selGear == -1) { if (carSpeed < cfg.ReverseSpeed) selGear = 0; }
                else if (selGear == 0) selGear = 1;
                else if (selGear < maxGears) { selGear++; justShifted = true; if (cfg.ThrottleCut) shiftUntil = nowMs + cfg.ShiftTimeMs; }
            }
            if (down && !downWas)
            {
                if (selGear > 0) { selGear--; justShifted = true; }
                else if (selGear == 0 && carSpeed < cfg.ReverseSpeed) selGear = -1;
            }
            upWas = up; downWas = down;

            // anti-stall: GTA IV gives no drive at all when the gear is too high for the speed
            // (revs stay at idle). Then shift down one gear every 300 ms until it pulls.
            if (sequential && cfg.AntiStall && selGear > 1 && inThrottle > 0.3f && engineRpm < 0.23f && nowMs > shiftUntil)
            {
                if (lugSince == 0) lugSince = nowMs;
                else if (nowMs - lugSince > 300) { selGear--; lugSince = 0; }
            }
            else lugSince = 0;
            reversing = selGear == -1;
            bool neutral = selGear == 0;

            // brake is always the brake; in R the throttle drives backwards; in N it only revs
            float gas = reversing ? -inThrottle : (neutral ? 0f : inThrottle);
            float brake = inBrake;
            if (nowMs < shiftUntil) gas = 0f;               // throttle cut while the gear changes

            // TorqueFill: GTA IV gives no drive when a gear is too high for the speed (revs at idle).
            // A real car still pulls there, just weakly. So the game is driven in a lower gear and the
            // throttle is scaled down by the gear-ratio difference, which gives about the pull of the
            // gear you selected. The display keeps showing your gear.
            GearSpeeds(veh);
            // clutch: 1 = engaged (drives), 0 = pedal pressed (disconnected)
            float pedalEngage = 1f - inClutch;
            if (cfg.ClutchMode == "Manual") clutchEngaged = pedalEngage;
            else clutchEngaged = Math.Min(nowMs < shiftUntil ? 0f : 1f, pedalEngage);

            // extra engine braking off the throttle: stronger in low gears and at high revs
            if (cfg.EngineBraking > 0f && inThrottle < 0.05f && selGear >= 1 && !neutral && clutchEngaged > 0.5f)
            {
                float r1 = gearSpeeds[1] > 0.1f ? gearSpeeds[1] : 3f;
                float rg = gearSpeeds[selGear] > 0.1f ? gearSpeeds[selGear] : r1 / selGear;
                float revs = Math.Max(0f, (engineRpm - 0.25f) / 0.75f);
                brake = Math.Min(1f, brake + cfg.EngineBraking * revs * (rg / r1) * clutchEngaged);
            }

            // Works from the game's own revs (0.2 = idle): if the gear the game is driven in sits at
            // idle, go one gear lower underneath; once the revs are up again, go back towards your gear.
            if (!sequential || !cfg.TorqueFill || selGear < 2 || reversing || neutral || clutchEngaged < 0.9f) driveGear = selGear;
            else
            {
                if (driveGear > selGear || driveGear < 1) driveGear = selGear;
                if (inThrottle < 0.05f)
                {
                    // off the throttle: coast in YOUR gear, so engine braking is that of your gear
                    driveGear = selGear; lowRevSince = 0;
                }
                else if (engineRpm < cfg.TorqueFillRpm && driveGear > 1)
                {
                    if (lowRevSince == 0) lowRevSince = nowMs;
                    else if (nowMs - lowRevSince > 120) { driveGear--; lowRevSince = 0; }
                }
                else
                {
                    lowRevSince = 0;
                    // step back up as soon as the next gear would no longer sit at idle
                    if (driveGear < selGear && gearSpeeds[driveGear] > 0.1f && gearSpeeds[driveGear + 1] > 0.1f)
                    {
                        float rpmNext = engineRpm * gearSpeeds[driveGear + 1] / gearSpeeds[driveGear];
                        if (rpmNext > cfg.TorqueFillRpm + 0.07f) driveGear++;
                    }
                    else if (driveGear < selGear && engineRpm > 0.75f) driveGear++;
                }
                if (driveGear < selGear)
                {
                    // pull of your gear relative to the lower gear. The handling's V_gearN values are
                    // gear RATIOS (screenshot 2026-11-26: about 3.x for 1st down to <1 for 5th), so the pull
                    // scales with ratio(your gear) / ratio(drive gear).
                    float ratio = (gearSpeeds[selGear] > 0.1f && gearSpeeds[driveGear] > 0.1f)
                        ? gearSpeeds[selGear] / gearSpeeds[driveGear] : (float)driveGear / selGear;
                    if (ratio > 1f) ratio = 1f;
                    gas *= ratio;
                }
            }

            steeringLock = veh.Handling != null ? veh.Handling.SteeringLock : 0.6f;
            // stored either in radians or degrees depending on version: treat small values as radians
            float lockRad = steeringLock > 3.2f ? steeringLock * (float)Math.PI / 180f : steeringLock;
            carLockDeg = lockRad * 180f / (float)Math.PI;
            float half = Math.Max(90f, cfg.WheelRotationDeg / 2f);
            lockWheelDeg = Math.Min(half, Math.Max(30f, carLockDeg * cfg.SteeringRatio / Math.Max(0.1f, cfg.SteerQuickness)));
            float wheelDeg = inSteer * half;                       // where the real wheel is, in degrees
            float frac = wheelDeg / lockWheelDeg;                  // -1..1 = full lock
            if (frac > 1f) frac = 1f;
            if (frac < -1f) frac = -1f;
            // GTA: positive steer = left, our input: positive = right
            float steer = -frac * lockRad;

            // clutch pedal: the game's clutch value can't be used, so the drive is scaled instead.
            // Pressed far enough = like neutral (no drive, engine revs freely).
            if (!neutral && !reversing && !patched)
            {
                gas *= Math.Max(0f, (clutchEngaged - 0.1f) / 0.9f);
            }
            veh.SteerActual = steer;
            veh.GasPedal = gas;
            veh.BrakePedal = brake;
            if (cfg.HandbrakeButton > 0) veh.VehicleFlags.IsHandbrakeOn = wheel.Button(cfg.HandbrakeButton);

            try
            {
                if (neutral || (clutchEngaged < 0.1f && !reversing))
                {
                    veh.EngineRPM = 0.2f + 0.8f * inThrottle;   // free revving (the game may override this)
                }
                if (sequential && !reversing && !neutral && cfg.GearForceMode != 2)
                {
                    veh.CurrentGear = (ushort)driveGear;
                    if (cfg.GearForceMode == 1) veh._f10e2 = (ushort)driveGear;
                    // NOTE: _f10f4 is the game's throttle copy, not the clutch. The clutch is _f10f0.
                    if (patched)
                    {
                        // the mod runs the clutch itself: a shift drops it to 0.1, then it comes back in
                        // over ClutchInTime seconds (GTA's own ramp was far too slow here)
                        float dtc = Math.Min(0.1f, Math.Max(0f, (nowMs - lastClutchMs) / 1000f));
                        if (justShifted) modClutch = 0.1f;
                        else modClutch = Math.Min(1f, modClutch + dtc * 0.9f / Math.Max(0.05f, cfg.ClutchInTime));
                        float pedal = 1f - inClutch;
                        if (pedal > 0.97f) pedal = 1f;                 // small dead zone at the top of the pedal
                        veh._f10f0 = Math.Min(modClutch, pedal);
                    }   // probably the gear the game wants next
                }
                gameGear = veh.CurrentGear; gameNextGear = veh._f10e2; engineRpm = veh.EngineRPM;
                gearDebug = string.Format(CultureInfo.InvariantCulture,
                    "revs {0:0.00}  e8 {1:0.00}  ec {2:0.00}  game clutch {3:0.00}  game throttle {4:0.00}  f8 {5}  100 {6:0.00}  104 {7:0.00}",
                    veh.EngineRevs, veh._f10e8, veh._f10ec, veh._f10f0, veh._f10f4, veh._f10f8, veh._f1100, veh._f1104);
            }
            catch { }
            justShifted = false;
            lastClutchMs = nowMs;

            outSteer = steer; outGas = gas; outBrake = brake;

            try
            {
                if (veh.Handling != null) tractionPeakRaw = veh.Handling.TractionCurveLateral;
                IVVehicleWheel[] ws = veh.Wheels;
                int n = ws == null ? 0 : (int)Math.Min((uint)ws.Length, veh.WheelCount);
                int front = 0, ground = 0;
                for (int i = 0; i < n; i++)
                {
                    if (ws[i] == null) continue;
                    if (ws[i].Flags.IsRearWheel) continue;
                    front++;
                    if (ws[i].Flags.NotInAir) ground++;
                }
                frontWheels = front; frontOnGround = ground;
                if (ground > 0) seenGrounded = true;
                frontGrounded = front > 0 ? (float)ground / front : 1f;
            }
            catch { frontWheels = 0; }
        }

        void Bar(string label, float value, float min, float max)
        {
            float f = (value - min) / (max - min);
            if (f < 0f) f = 0f;
            if (f > 1f) f = 1f;
            ImGuiIV.TextUnformatted(string.Format("{0,-9}", label));
            ImGuiIV.SameLine();
            ImGuiIV.ProgressBar(f, new Vector2(220f, 0f), value.ToString("0.00", CultureInfo.InvariantCulture));
        }

        bool hudOpen = true;
        volatile bool hudAllowed = true;
        volatile bool controlOn = true;
        readonly ButtonMapper buttons = new ButtonMapper();
        readonly ButtonWizard wizard = new ButtonWizard();
        int buttonsCar = 0;
        volatile int blinkTestMask = -1;
        bool indicatorWasSet;
        int lastIndicator, blinkStart;   // false while the game has taken control (death, arrest, cutscene)

        // Gear, rev counter and speed, bottom right of the screen
        void DrawHud()
        {
            if (!cfg.ShowHud || !enabled || !playerDriving || !wheelTookOver || !hudAllowed) return;
            Vector2 ds = ImGuiIV.GetIO().DisplaySize;
            ImGuiIV.SetNextWindowPos(new Vector2(ds.X - 640f, ds.Y - 150f), eImGuiCond.Always);
            ImGuiIV.SetNextWindowBgAlpha(0.35f);
            eImGuiWindowFlags fl = eImGuiWindowFlags.NoDecoration | eImGuiWindowFlags.NoInputs | eImGuiWindowFlags.AlwaysAutoResize |
                eImGuiWindowFlags.NoSavedSettings | eImGuiWindowFlags.NoFocusOnAppearing | eImGuiWindowFlags.NoNav |
                eImGuiWindowFlags.NoBringToFrontOnFocus;
            hudOpen = true;
            if (ImGuiIV.Begin("WheelModHud", ref hudOpen, fl))
            {
                string g = selGear == -1 ? "R" : selGear == 0 ? "N" : (cfg.GearboxMode == "Automatic" ? "D" : selGear.ToString());
                ImGuiIV.SetWindowFontScale(3.5f);
                ImGuiIV.TextUnformatted(g);
                ImGuiIV.SameLine();
                ImGuiIV.SetWindowFontScale(1.6f);
                ImGuiIV.TextUnformatted(string.Format(CultureInfo.InvariantCulture, "  {0:0} km/h", Math.Abs(carSpeed) * 3.6f));
                float rpm = Math.Max(0f, Math.Min(1f, engineRpm));   // GTA: 0.2 = idle, 1.0 = limiter
                Vector4 col = rpm >= cfg.ShiftLightRpm ? ((Environment.TickCount / 80) % 2 == 0 ? new Vector4(1f, 0.1f, 0.1f, 1f) : new Vector4(1f, 1f, 1f, 1f))
                    : rpm >= cfg.ShiftLightRpm - 0.15f ? new Vector4(1f, 0.75f, 0.1f, 1f) : new Vector4(0.2f, 0.85f, 0.3f, 1f);
                ImGuiIV.PushStyleColor(eImGuiCol.PlotHistogram, col);
                ImGuiIV.ProgressBar(rpm, new Vector2(260f, 22f), string.Format(CultureInfo.InvariantCulture, "{0:0} rpm",
                    cfg.IdleRpm + Math.Max(0f, rpm - 0.2f) / 0.8f * (cfg.RedlineRpm - cfg.IdleRpm)));
                ImGuiIV.PopStyleColor();
                ImGuiIV.SetWindowFontScale(1f);
                // TEMPORARY test info (build 3d) - remove once the gearbox works
                ImGuiIV.TextUnformatted(string.Format(CultureInfo.InvariantCulture,
                    "build 3w  patch {11}  clutch {10:0.00}  drive gear {0}  game gear {1} / next {2}\nrpm {3:0.00}  gas {4:0.00}  ratios {5:0.00} {6:0.00} {7:0.00} {8:0.00} {9:0.00}",
                    driveGear, gameGear, gameNextGear, engineRpm, outGas, gearSpeeds[1], gearSpeeds[2], gearSpeeds[3], gearSpeeds[4], gearSpeeds[5], clutchEngaged, GearboxPatch.Active ? "ON" : "off"));
                ImGuiIV.TextUnformatted(gearDebug);
            }
            ImGuiIV.End();
        }

        void Main_OnImGuiRendering(IntPtr devicePtr, ImGuiIV_DrawingContext ctx)
        {
            DrawHud();
            if (hudAllowed && playerDriving) wizard.Draw(cfg.ButtonMap);
            if (!showWindow || !hudAllowed) return;   // the status window also steps aside for game prompts

            windowOpen = true;
            ImGuiIV.SetNextWindowSize(new Vector2(380f, 0f), eImGuiCond.FirstUseEver);
            if (ImGuiIV.Begin("Wheel Mod   F6 hide | F7 on/off", ref windowOpen, eImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGuiIV.TextUnformatted("Wheel: " + (enabled ? "ON" : "OFF") + "   Device: " +
                    (wheel != null ? wheel.Name : "not found (VID " + cfg.Vid.ToString("X4") + " PID " + cfg.Pid.ToString("X4") + ")"));
                ImGuiIV.TextUnformatted("Driving: " + (playerDriving ? "yes" : "no") +
                    "   Wheel in control: " + (wheelTookOver ? "yes" : "no (move the wheel)") +
                    "");
                ImGuiIV.TextUnformatted(string.Format(CultureInfo.InvariantCulture, "Speed: {0:0.0} m/s   Car lock: {1:0.0} deg (raw {2:0.00})   Full lock at: {3:0} deg of wheel", carSpeed, carLockDeg, steeringLock, lockWheelDeg));
                ImGuiIV.TextUnformatted(string.Format(CultureInfo.InvariantCulture,
                    "Gear: {0}   ({1}, {2} gears)   game gear {3} / next {4}   rpm {5:0.00}",
                    selGear == -1 ? "R" : selGear == 0 ? "N" : (cfg.GearboxMode == "Automatic" ? "D" : selGear.ToString()),
                    cfg.GearboxMode, maxGears, gameGear, gameNextGear, engineRpm));
                ImGuiIV.TextUnformatted("Last wheel button: " + buttons.LastPressed + "   last action: " + buttons.LastAction);
                ImGuiIV.TextUnformatted("Gearbox patch: " + GearboxPatch.Status);
                ImGuiIV.TextUnformatted("Gearbox data: " + gearDebug);
                ImGuiIV.TextUnformatted("Force feedback: " + (cfg.FfbEnabled ? ffb.Status : "off in ini"));
                if (cfg.FfbEnabled)
                {
                    ImGuiIV.TextUnformatted("FFB codes: " + ffb.Diag + "   (all 00000000 = OK)");
                    Bar("FFB force", lastForce, -1f, 1f);
                    ImGuiIV.TextUnformatted(string.Format(CultureInfo.InvariantCulture,
                        "Car: forward {0:0.0} m/s  sideways {1:0.0} m/s  yaw {2:0.00} rad/s  front slip {3:0.0} deg (grip peak {4:0.0} deg, raw {5:0.###})",
                        carLong, carLat, yawRight, slipDeg, peakDeg, tractionPeakRaw));
                    ImGuiIV.TextUnformatted(string.Format(CultureInfo.InvariantCulture,
                        "Front wheels on the ground: {0}/{1}   last impact {2:0.0} g", frontOnGround, frontWheels, impactG));
                    Bar("Tyres", fSat, -1f, 1f);
                    Bar("Spring", fSpring, -1f, 1f);
                    Bar("Damper", fDamper, -1f, 1f);
                    Bar("Bumps", fDetail, -1f, 1f);
                    Bar("Crash", fCrash, -1f, 1f);
                    Bar("Lock", fLock, -1f, 1f);
                }
                ImGuiIV.Separator();
                ImGuiIV.TextUnformatted("Input");
                Bar("Steer", inSteer, -1f, 1f);
                Bar("Throttle", inThrottle, 0f, 1f);
                Bar("Brake", inBrake, 0f, 1f);
                ImGuiIV.TextUnformatted("Sent to car");
                Bar("Steer", outSteer, -1f, 1f);
                Bar("Gas", outGas, -1f, 1f);
                Bar("Brake", outBrake, 0f, 1f);
            }
            ImGuiIV.End();
            if (!windowOpen) showWindow = false;
        }
    }
}

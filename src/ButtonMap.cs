// GTA IV Wheel Mod - mappable wheel buttons and D-pad
// Copyright (c) 2026 hommedenoir
//
// Every wheel button (Button1..Button32) and D-pad direction (PovUp, PovRight,
// PovDown, PovLeft) can get an action in WheelMod.ini, section [ButtonMap]:
//
//   none          nothing
//   Horn          holds the game's horn key (HornKey in the ini): tap = siren, hold = horn / yelp
//   Hazards       hazard lights on/off
//   IndicatorLeft / IndicatorRight   turn signal on/off (the other side switches over)
//   Headlights    lights: auto -> on -> off -> auto
//   Siren         siren on/off (police cars, ambulances, fire trucks)
//   InteriorLight interior light on/off
//   WheelOnOff    same as F7
//   Key:<name>    holds a keyboard key while the button is held, e.g. Key:Up (phone),
//                 Key:V (camera), Key:F (exit), Key:Space (handbrake), Key:Q (radio)
//                 Names as in .NET's Keys list: A-Z, D0-D9, Up, Down, Left, Right,
//                 Space, Return, Escape, Tab, LShiftKey, LControlKey, F1-F12, ...

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

using IVSDKDotNet;
using IVSDKDotNet.Native;

namespace WheelMod
{
    class ButtonMapper
    {
        public static readonly string[] PovNames = { "PovUp", "PovRight", "PovDown", "PovLeft" };
        public const int MaxButtons = 32;

        // input name (lower case) -> currently held
        readonly Dictionary<string, bool> was = new Dictionary<string, bool>();
        // keys this mapper is holding down right now, by input name
        readonly Dictionary<string, ushort> heldKeys = new Dictionary<string, ushort>();
        int lightsState = 0;        // 0 auto, 1 on, 2 off
        bool interior;
        // read by the game thread (ProcessAutomobile), which writes them into the car every frame
        public volatile int Indicator;      // -1 left, 0 off, 1 right
        public volatile bool Hazards;
        public Keys HornKey = Keys.G;   // the game's horn key (GTA IV PC: G); set from WheelMod.ini

        public string LastPressed = "-";
        public string LastAction = "-";

        public bool WheelToggleRequested;

        // ---- keyboard via SendInput (scan codes, so DirectInput games see them)
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)]
        struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public INPUTUNION u; }

        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);

        const uint INPUT_KEYBOARD = 1, KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2, KEYEVENTF_SCANCODE = 8;

        static bool IsExtended(Keys k)
        {
            switch (k)
            {
                case Keys.Up: case Keys.Down: case Keys.Left: case Keys.Right:
                case Keys.Insert: case Keys.Delete: case Keys.Home: case Keys.End:
                case Keys.PageUp: case Keys.PageDown: case Keys.RControlKey: case Keys.RMenu:
                case Keys.Divide: case Keys.NumLock:
                    return true;
            }
            return false;
        }

        static void SendKey(Keys k, bool down)
        {
            ushort scan = (ushort)MapVirtualKey((uint)k, 0);
            if (scan == 0) return;
            INPUT[] inp = new INPUT[1];
            inp[0].type = INPUT_KEYBOARD;
            inp[0].u.ki.wScan = scan;
            inp[0].u.ki.dwFlags = KEYEVENTF_SCANCODE | (IsExtended(k) ? KEYEVENTF_EXTENDEDKEY : 0) | (down ? 0 : KEYEVENTF_KEYUP);
            SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
        }

        static bool TryKey(string action, out Keys key)
        {
            key = Keys.None;
            if (!action.StartsWith("key:", StringComparison.OrdinalIgnoreCase)) return false;
            try { key = (Keys)Enum.Parse(typeof(Keys), action.Substring(4).Trim(), true); }
            catch { return false; }
            return key != Keys.None;
        }

        // ---- POV: hundredths of a degree, 0 = up, 9000 = right; diagonals press both
        static bool PovDir(int pov, int dir)
        {
            if (pov < 0 || pov > 35999) return false;
            int centre = dir * 9000;
            int d = Math.Abs(pov - centre);
            if (d > 18000) d = 36000 - d;
            return d <= 6750;
        }

        // Called from the script thread every tick while the player drives
        public void Update(WheelDevice wheel, Dictionary<string, string> map, int veh)
        {
            foreach (KeyValuePair<string, string> e in map)
            {
                string input = e.Key;
                bool now = IsDown(wheel, input);
                bool before;
                was.TryGetValue(input, out before);
                was[input] = now;

                if (now && !before) LastPressed = Pretty(input);

                string action = e.Value ?? "none";
                Keys key;
                if (TryKey(action, out key))
                {
                    if (now && !before) { SendKey(key, true); heldKeys[input] = (ushort)key; LastAction = "key " + key; }
                    else if (!now && before) { SendKey(key, false); heldKeys.Remove(input); }
                    continue;
                }

                string a = action.ToLowerInvariant();
                if (a == "horn")
                {
                    // hold the game's own horn key, so the game does what the real horn does:
                    // tap = siren on/off in emergency cars, hold = yelp / horn
                    if (now && !before) { SendKey(HornKey, true); heldKeys[input] = (ushort)HornKey; LastAction = "horn (" + HornKey + ")"; }
                    else if (!now && before) { SendKey(HornKey, false); heldKeys.Remove(input); }
                    continue;
                }
                if (!(now && !before)) continue;   // the rest act once per press

                switch (a)
                {
                    case "hazards":
                        Hazards = !Hazards;
                        if (veh != 0) Natives.SET_VEH_HAZARDLIGHTS(veh, Hazards);
                        LastAction = "hazards " + (Hazards ? "on" : "off");
                        break;
                    case "indicatorleft":
                        Indicator = Indicator == -1 ? 0 : -1;
                        LastAction = "indicator " + (Indicator == -1 ? "left" : "off");
                        break;
                    case "indicatorright":
                        Indicator = Indicator == 1 ? 0 : 1;
                        LastAction = "indicator " + (Indicator == 1 ? "right" : "off");
                        break;
                    case "headlights":
                        lightsState = (lightsState + 1) % 3;
                        // FORCE_CAR_LIGHTS: 0 = game decides, 1 = always off, 2 = always on
                        if (veh != 0) Natives.FORCE_CAR_LIGHTS(veh, lightsState == 1 ? 2 : lightsState == 2 ? 1 : 0);
                        LastAction = "lights " + (lightsState == 0 ? "auto" : lightsState == 1 ? "on" : "off");
                        IVGame.ShowSubtitleMessage("Lights: " + (lightsState == 0 ? "auto" : lightsState == 1 ? "on" : "off"));
                        break;
                    case "siren":
                        if (veh != 0) Natives.SWITCH_CAR_SIREN(veh, !Natives.IS_CAR_SIREN_ON(veh));
                        LastAction = "siren";
                        break;
                    case "interiorlight":
                        interior = !interior;
                        if (veh != 0) Natives.SET_VEH_INTERIORLIGHT(veh, interior);
                        LastAction = "interior light " + (interior ? "on" : "off");
                        break;
                    case "wheelonoff":
                        WheelToggleRequested = true;
                        LastAction = "wheel on/off";
                        break;
                }
            }
        }

        // Watch every button even if it has no action, so the status window can show its name
        public void Track(WheelDevice wheel)
        {
            for (int b = 1; b <= MaxButtons; b++)
            {
                string n = "button" + b;
                bool now = wheel.Button(b), before;
                was.TryGetValue("t_" + n, out before);
                was["t_" + n] = now;
                if (now && !before) LastPressed = Pretty(n);
            }
            for (int d = 0; d < 4; d++)
            {
                string n = PovNames[d].ToLowerInvariant();
                bool now = PovDir(wheel.Pov, d), before;
                was.TryGetValue("t_" + n, out before);
                was["t_" + n] = now;
                if (now && !before) LastPressed = PovNames[d];
            }
        }

        static bool IsDown(WheelDevice wheel, string input)
        {
            if (input.StartsWith("pov"))
            {
                int d = Array.FindIndex(PovNames, p => p.ToLowerInvariant() == input);
                return d >= 0 && PovDir(wheel.Pov, d);
            }
            int n;
            if (input.StartsWith("button") && int.TryParse(input.Substring(6), out n)) return wheel.Button(n);
            return false;
        }

        static string Pretty(string input)
        {
            if (input.StartsWith("button")) return "Button" + input.Substring(6);
            int d = Array.FindIndex(PovNames, p => p.ToLowerInvariant() == input);
            return d >= 0 ? PovNames[d] : input;
        }

        // Let go of any keys we are holding (leaving the car, wheel off, game took control)
        public void ReleaseAll()
        {
            foreach (KeyValuePair<string, ushort> h in heldKeys) SendKey((Keys)h.Value, false);
            heldKeys.Clear();
            List<string> keys = new List<string>(was.Keys);
            foreach (string k in keys) if (!k.StartsWith("t_")) was[k] = false;
        }

        // A new car: the game's own lights and hazards start fresh
        public void NewCar()
        {
            lightsState = 0; Hazards = false; interior = false; Indicator = 0;
        }
    }
}

namespace WheelMod
{
    // One-time setup dialog: asks for a wheel button per action, using only the wheel.
    // Right paddle = skip (keep what is set), left paddle = back.
    class ButtonWizard
    {
        static readonly string[] Labels = { "Horn", "Hazard lights", "Indicator left", "Indicator right", "Headlights (auto / on / off)", "Siren",
                                            "Phone (Up arrow)", "Change camera (V)", "Get out of the car (F)", "Wheel on / off (F7)" };
        static readonly string[] Actions = { "Horn", "Hazards", "IndicatorLeft", "IndicatorRight", "Headlights", "Siren", "Key:Up", "Key:V", "Key:F", "WheelOnOff" };

        public bool Active;
        public bool Finished;      // set once when the last step is done; the script saves the ini
        int step;
        bool first;                                   // first tick: remember what is already held, don't take it
        readonly List<string> before = new List<string>();
        bool upWas, downWas;
        string lastAssigned = "";

        public void Start() { Active = true; Finished = false; step = 0; first = true; lastAssigned = ""; }

        // F8 while open: close and count the setup as done (what was chosen so far is kept)
        public void Close() { Active = false; Finished = true; }

        static List<string> Pressed(WheelDevice w, int skipA, int skipB)
        {
            List<string> p = new List<string>();
            for (int b = 1; b <= ButtonMapper.MaxButtons; b++)
                if (b != skipA && b != skipB && w.Button(b)) p.Add("button" + b);
            int pov = w.Pov;
            if (pov >= 0 && pov <= 35999)
            {
                int d = ((pov + 4500) / 9000) % 4;           // nearest direction
                p.Add(ButtonMapper.PovNames[d].ToLowerInvariant());
            }
            return p;
        }

        public void Update(WheelDevice w, Dictionary<string, string> map, int upButton, int downButton)
        {
            if (!Active) return;
            List<string> pressed = Pressed(w, upButton, downButton);
            bool up = upButton > 0 && w.Button(upButton), down = downButton > 0 && w.Button(downButton);
            // only NEW presses count; a button or switch that is held all the time is ignored
            string input = null;
            if (!first)
                foreach (string p in pressed) if (!before.Contains(p)) { input = p; break; }
            bool upNew = up && !upWas && !first, downNew = down && !downWas && !first;
            before.Clear(); before.AddRange(pressed);
            upWas = up; downWas = down; first = false;

            if (upNew) { Next(); return; }
            if (downNew) { if (step > 0) step--; return; }
            if (input == null) return;

            string action = Actions[step];
            // this action leaves any other button it was on
            List<string> keys = new List<string>(map.Keys);
            foreach (string k in keys)
                if (string.Equals(map[k], action, StringComparison.OrdinalIgnoreCase)) map[k] = "none";
            map[input] = action;
            lastAssigned = Labels[step] + " -> " + ButtonPretty(input);
            Next();
        }

        void Next()
        {
            step++;
            if (step >= Actions.Length) { Active = false; Finished = true; }
        }

        static string ButtonPretty(string input)
        {
            if (input.StartsWith("button")) return "Button" + input.Substring(6);
            foreach (string p in ButtonMapper.PovNames) if (p.ToLowerInvariant() == input) return "D-pad " + p.Substring(3).ToLowerInvariant();
            return input;
        }

        static string CurrentFor(Dictionary<string, string> map, string action)
        {
            foreach (KeyValuePair<string, string> e in map)
                if (string.Equals(e.Value, action, StringComparison.OrdinalIgnoreCase)) return ButtonPretty(e.Key);
            return "not set";
        }

        // Drawn from OnImGuiRendering. Takes no mouse or keyboard input.
        public void Draw(Dictionary<string, string> map)
        {
            if (!Active) return;
            System.Numerics.Vector2 ds = ImGuiIV.GetIO().DisplaySize;
            ImGuiIV.SetNextWindowPos(new System.Numerics.Vector2(ds.X * 0.5f - 260f, ds.Y * 0.3f), IVSDKDotNet.Enums.eImGuiCond.Always);
            ImGuiIV.SetNextWindowBgAlpha(0.85f);
            bool open = true;
            IVSDKDotNet.Enums.eImGuiWindowFlags fl = IVSDKDotNet.Enums.eImGuiWindowFlags.NoDecoration | IVSDKDotNet.Enums.eImGuiWindowFlags.NoInputs |
                IVSDKDotNet.Enums.eImGuiWindowFlags.AlwaysAutoResize | IVSDKDotNet.Enums.eImGuiWindowFlags.NoSavedSettings |
                IVSDKDotNet.Enums.eImGuiWindowFlags.NoFocusOnAppearing | IVSDKDotNet.Enums.eImGuiWindowFlags.NoNav;
            if (ImGuiIV.Begin("WheelModButtonSetup", ref open, fl))
            {
                ImGuiIV.TextUnformatted("WHEEL BUTTON SETUP   (" + (step + 1) + " of " + Actions.Length + ")");
                ImGuiIV.Separator();
                ImGuiIV.TextUnformatted("Press the wheel button or D-pad direction for:");
                ImGuiIV.TextUnformatted("    " + Labels[step]);
                ImGuiIV.TextUnformatted("Now on: " + CurrentFor(map, Actions[step]));
                ImGuiIV.Separator();
                ImGuiIV.TextUnformatted("Right paddle = skip (keep it)     Left paddle = back");
                if (lastAssigned.Length > 0) ImGuiIV.TextUnformatted("Saved: " + lastAssigned);
                ImGuiIV.TextUnformatted("F8 = close (keeps what you set). F8 in a car opens it again.");
            }
            ImGuiIV.End();
        }
    }
}

// Minimal DirectInput 8 force feedback for the GTA IV Wheel Mod.
// Talks to dinput8.dll directly through the COM vtables (no extra libraries needed).
// Uses ONE constant-force effect; centering, damping and soft lock are calculated by the mod
// and sent as a single force many times per second (the same approach sims use).

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WheelMod
{
    class ForceFeedback : IDisposable
    {
        // ------------------------------------------------------------ native bits
        [DllImport("dinput8.dll")]
        static extern int DirectInput8Create(IntPtr hinst, int version, ref Guid riid, out IntPtr ppvOut, IntPtr punkOuter);
        [DllImport("kernel32.dll")]
        static extern IntPtr GetModuleHandle(IntPtr name);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ReleaseFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateDeviceFn(IntPtr self, ref Guid guid, out IntPtr dev, IntPtr outer);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumDevicesFn(IntPtr self, int devType, EnumCallback cb, IntPtr pvRef, int flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumCallback(IntPtr lpddi, IntPtr pvRef);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetPropertyFn(IntPtr self, IntPtr guidProp, IntPtr diph);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int NoArgFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetDataFormatFn(IntPtr self, IntPtr fmt);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetCoopFn(IntPtr self, IntPtr hwnd, int flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateEffectFn(IntPtr self, ref Guid guid, IntPtr eff, out IntPtr effect, IntPtr outer);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetParamsFn(IntPtr self, IntPtr eff, int flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int StartFn(IntPtr self, int iterations, int flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SendCmdFn(IntPtr self, int cmd);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetFfStateFn(IntPtr self, out int state);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetObjectInfoFn(IntPtr self, IntPtr info, int obj, int how);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetDeviceStateFn(IntPtr self, int cb, IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetEffStatusFn(IntPtr self, out int status);

        // Struct layouts differ between 32-bit and 64-bit processes (pointer size)
        static readonly int P = IntPtr.Size;
        static int Align(int v, int a) { return (v + a - 1) / a * a; }
        static readonly int EFF_rgdwAxes = 32, EFF_rglDirection = 32 + P, EFF_lpEnvelope = 32 + 2 * P,
            EFF_cbTSP = 32 + 3 * P, EFF_lpvTSP = Align(32 + 3 * P + 4, P), EFF_dwStartDelay = Align(32 + 3 * P + 4, P) + P,
            EFF_Size = Align(Align(32 + 3 * P + 4, P) + P + 4, P);
        static readonly int ODF_Size = Align(P + 12, P), DF_Size = Align(20, P) + P, DF_rgodf = Align(20, P);

        static T Vt<T>(IntPtr obj, int slot) where T : class
        {
            IntPtr vtbl = Marshal.ReadIntPtr(obj);
            IntPtr fn = Marshal.ReadIntPtr(vtbl, slot * IntPtr.Size);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(fn, typeof(T));
        }

        // vtable slots (IUnknown = 0..2)
        const int DI_CreateDevice = 3, DI_EnumDevices = 4;
        const int DEV_SetProperty = 6, DEV_Acquire = 7, DEV_Unacquire = 8, DEV_SetDataFormat = 11,
                  DEV_SetCooperativeLevel = 13, DEV_CreateEffect = 18, DEV_SendFFCommand = 22, DEV_Poll = 25;
        const int EFF_SetParameters = 6, EFF_Start = 7, EFF_Stop = 8;

        static readonly Guid IID_IDirectInput8W = new Guid("BF798031-483A-4DA2-AA99-5D64ED369700");
        static readonly Guid GUID_ConstantForce = new Guid("13541C20-8E33-11D0-9AD0-00A0C9A06E35");

        const int DI8DEVCLASS_GAMECTRL = 4, DIEDFL_ATTACHEDONLY = 1, DIEDFL_FORCEFEEDBACK = 0x100;
        const int DISCL_EXCLUSIVE = 1, DISCL_FOREGROUND = 4, DISCL_BACKGROUND = 8;
        const int DIEFF_OBJECTOFFSETS = 0x2, DIEFF_CARTESIAN = 0x10;
        const int DIEP_TYPESPECIFICPARAMS = 0x100,  // was 0x200 by mistake (= DIEP_STARTDELAY), so the force never changed
                  DIEP_DIRECTION = 0x40, DIEP_START = 0x20000000;
        const int DISFFC_RESET = 1, DISFFC_STOPALL = 2, DISFFC_SETACTUATORSON = 0x10;
        const int INFINITE = -1, DIEB_NOTRIGGER = -1;

        // ------------------------------------------------------------ state
        IntPtr di, dev, constant;
        IntPtr dataFormat, objFormats, guidBlock;
        IntPtr effectBuf, forceBuf, axesBuf, dirBuf;
        Guid foundGuid;
        bool found;
        ushort wantVid, wantPid;
        EnumCallback keepAlive;
        int hrCreate, hrStart, hrSet;
        int lastMagnitude = int.MinValue;
        int kickUntil = 0, kickMagnitude = 0, reacquires = 0;
        public int updates = 0;

        public bool Ready { get; private set; }
        // direction value for the single-axis effect (some drivers ignore 0)
        public int DirectionValue = 1;
        // How the force is updated. Test 2026-11-26: re-sending DIEP_START every frame (the old way)
        // never moved the T248; an effect started ONCE does move it.
        public bool RestartEachUpdate = false;   // old behaviour: DIEP_START on every update
        public bool SendDirectionEachUpdate = false;
        public int EffectDurationUs = INFINITE;  // INFINITE or microseconds
        public int CreateMagnitude = 0;          // force the effect is created with (-10000..10000)
        public bool CoopForeground = false;   // DISCL_FOREGROUND instead of DISCL_BACKGROUND
        public bool UseObjectIds = false;     // address the axis by object ID instead of data offset
        int axisObjectId = 0;
        public string AxisInfo = "";
        public int LastDownload = 0;
        public string Status { get; private set; }
        public int LastForce { get { return lastMagnitude == int.MinValue ? 0 : lastMagnitude; } }
        public string Diag { get { return string.Format("create {0:X8}  start {1:X8}  last update {2:X8}  re-acquired {3}x  updates {4}", hrCreate, hrStart, hrSet, reacquires, updates); } }

        public ForceFeedback()
        {
            Status = "not started";
        }

        // ------------------------------------------------------------ setup
        public bool Init(ushort vid, ushort pid)
        {
            return Init(vid, pid, IntPtr.Zero);
        }

        public bool Init(ushort vid, ushort pid, IntPtr windowOverride)
        {
            Dispose();
            hrCreate = hrStart = hrSet = 0;
            try
            {
                IntPtr hwnd = windowOverride != IntPtr.Zero ? windowOverride : Process.GetCurrentProcess().MainWindowHandle;
                if (hwnd == IntPtr.Zero) { Status = "game window not found yet"; return false; }

                Guid iid = IID_IDirectInput8W;
                int hr = DirectInput8Create(GetModuleHandle(IntPtr.Zero), 0x0800, ref iid, out di, IntPtr.Zero);
                if (hr != 0) { Status = "DirectInput8Create failed " + hr.ToString("X8"); return false; }

                wantVid = vid; wantPid = pid; found = false;
                keepAlive = OnDevice;
                Vt<EnumDevicesFn>(di, DI_EnumDevices)(di, DI8DEVCLASS_GAMECTRL, keepAlive, IntPtr.Zero, DIEDFL_ATTACHEDONLY | DIEDFL_FORCEFEEDBACK);
                if (!found) { Status = "no force feedback device with this VID/PID found by DirectInput"; return false; }

                Guid g = foundGuid;
                hr = Vt<CreateDeviceFn>(di, DI_CreateDevice)(di, ref g, out dev, IntPtr.Zero);
                if (hr != 0) { Status = "CreateDevice failed " + hr.ToString("X8"); return false; }

                BuildDataFormat();
                hr = Vt<SetDataFormatFn>(dev, DEV_SetDataFormat)(dev, dataFormat);
                if (hr != 0) { Status = "SetDataFormat failed " + hr.ToString("X8"); return false; }
                ReadAxisInfo();

                hr = Vt<SetCoopFn>(dev, DEV_SetCooperativeLevel)(dev, hwnd, DISCL_EXCLUSIVE | (CoopForeground ? DISCL_FOREGROUND : DISCL_BACKGROUND));
                if (hr != 0) { Status = "SetCooperativeLevel failed " + hr.ToString("X8"); return false; }

                SetAutoCenter(false);
                SetDeviceGain(10000);

                hr = Vt<NoArgFn>(dev, DEV_Acquire)(dev);
                if (hr != 0 && hr != 1) { Status = "Acquire failed " + hr.ToString("X8"); return false; }

                Vt<SendCmdFn>(dev, DEV_SendFFCommand)(dev, DISFFC_SETACTUATORSON);

                AllocEffectBuffers();
                FillConstant(CreateMagnitude);
                lastMagnitude = CreateMagnitude;
                Guid cg = GUID_ConstantForce;
                hr = Vt<CreateEffectFn>(dev, DEV_CreateEffect)(dev, ref cg, effectBuf, out constant, IntPtr.Zero);
                if (hr != 0) { hrCreate = hr; Status = "could not create the force effect (" + Diag + ")"; return false; }
                hr = Vt<StartFn>(constant, EFF_Start)(constant, 1, 0);
                if (hr != 0) hrStart = hr;
                LastDownload = Vt<NoArgFn>(constant, 10)(constant); // IDirectInputEffect::Download

                Ready = true;
                Status = "running";
                return true;
            }
            catch (Exception ex)
            {
                Status = "error: " + ex.Message;
                return false;
            }
        }

        public System.Collections.Generic.List<string> SeenDevices = new System.Collections.Generic.List<string>();

        int OnDevice(IntPtr lpddi, IntPtr pvRef)
        {
            try
            {
                string instName = Marshal.PtrToStringUni(lpddi + 40);   // tszInstanceName
                string prodName = Marshal.PtrToStringUni(lpddi + 560);  // tszProductName
                byte[] pr = new byte[16];
                Marshal.Copy(lpddi + 20, pr, 0, 16);
                uint d1 = BitConverter.ToUInt32(pr, 0);
                SeenDevices.Add(string.Format("{0} / {1}  (VID {2:X4} PID {3:X4})", instName, prodName, d1 & 0xFFFF, d1 >> 16));
            }
            catch { }
            // DIDEVICEINSTANCEW: dwSize, guidInstance (offset 4), guidProduct (offset 20)
            byte[] inst = new byte[16], prod = new byte[16];
            Marshal.Copy(lpddi + 4, inst, 0, 16);
            Marshal.Copy(lpddi + 20, prod, 0, 16);
            uint data1 = BitConverter.ToUInt32(prod, 0); // MAKELONG(VID, PID)
            if (!found && (data1 & 0xFFFF) == wantVid && (data1 >> 16) == wantPid)
            {
                foundGuid = new Guid(inst);
                found = true;
            }
            return 1; // DIENUM_CONTINUE: keep listing so the test can show every device
        }

        // DIDEVICEOBJECTINSTANCEW for the X axis (by offset 0): is it a force feedback actuator?
        void ReadAxisInfo()
        {
            IntPtr p = Marshal.AllocHGlobal(576);
            try
            {
                for (int i = 0; i < 576; i += 4) Marshal.WriteInt32(p, i, 0);
                Marshal.WriteInt32(p, 0, 576);
                int hr = Vt<GetObjectInfoFn>(dev, 14)(dev, p, 0, 1); // DIPH_BYOFFSET
                if (hr != 0) { AxisInfo = "GetObjectInfo failed " + hr.ToString("X8"); return; }
                int type = Marshal.ReadInt32(p, 24);
                int flags = Marshal.ReadInt32(p, 28);
                string name = Marshal.PtrToStringUni(p + 32);
                int maxForce = Marshal.ReadInt32(p, 552);
                axisObjectId = type;
                AxisInfo = string.Format("X axis '{0}' type 0x{1:X8} flags 0x{2:X} {3}{4} maxForce {5}", name, type, flags,
                    (type & 0x01000000) != 0 ? "[FF-ACTUATOR] " : "[not an FF actuator] ",
                    (flags & 0x1) != 0 ? "[FF-capable] " : "", maxForce);
            }
            finally { Marshal.FreeHGlobal(p); }
        }

        void SetAutoCenter(bool on)
        {
            IntPtr p = Marshal.AllocHGlobal(20);
            try
            {
                Marshal.WriteInt32(p, 0, 20); Marshal.WriteInt32(p, 4, 16);
                Marshal.WriteInt32(p, 8, 0); Marshal.WriteInt32(p, 12, 0);
                Marshal.WriteInt32(p, 16, on ? 1 : 0);
                Vt<SetPropertyFn>(dev, DEV_SetProperty)(dev, new IntPtr(9), p); // DIPROP_AUTOCENTER
            }
            finally { Marshal.FreeHGlobal(p); }
        }

        // DIPROP_FFGAIN = MAKEDIPROP(7): overall force strength of the device, 0..10000
        void SetDeviceGain(int gain)
        {
            IntPtr p = Marshal.AllocHGlobal(20);
            try
            {
                Marshal.WriteInt32(p, 0, 20); Marshal.WriteInt32(p, 4, 16);
                Marshal.WriteInt32(p, 8, 0); Marshal.WriteInt32(p, 12, 0);
                Marshal.WriteInt32(p, 16, gain);
                Vt<SetPropertyFn>(dev, DEV_SetProperty)(dev, new IntPtr(7), p);
            }
            finally { Marshal.FreeHGlobal(p); }
        }

        // Same layout as c_dfDIJoystick (DIJOYSTATE, 80 bytes)
        void BuildDataFormat()
        {
            Guid[] guids =
            {
                new Guid("A36D02E0-C9F3-11CF-BFC7-444553540000"), // X
                new Guid("A36D02E1-C9F3-11CF-BFC7-444553540000"), // Y
                new Guid("A36D02E2-C9F3-11CF-BFC7-444553540000"), // Z
                new Guid("A36D02F4-C9F3-11CF-BFC7-444553540000"), // Rx
                new Guid("A36D02F5-C9F3-11CF-BFC7-444553540000"), // Ry
                new Guid("A36D02E3-C9F3-11CF-BFC7-444553540000"), // Rz
                new Guid("A36D02E4-C9F3-11CF-BFC7-444553540000"), // Slider
                new Guid("A36D02F2-C9F3-11CF-BFC7-444553540000"), // POV
            };
            guidBlock = Marshal.AllocHGlobal(16 * guids.Length);
            for (int i = 0; i < guids.Length; i++) Marshal.Copy(guids[i].ToByteArray(), 0, guidBlock + i * 16, 16);

            const int numObjs = 6 + 2 + 4 + 32;
            int objSize = ODF_Size;
            const int AXIS = unchecked((int)0x80FFFF03), POV = unchecked((int)0x80FFFF10), BUTTON = unchecked((int)0x80FFFF0C);
            const int ASPECTPOSITION = 0x100;

            objFormats = Marshal.AllocHGlobal(numObjs * objSize);
            int n = 0;
            Action<IntPtr, int, int, int> add = (pguid, ofs, type, flags) =>
            {
                IntPtr o = objFormats + n * objSize;
                for (int b = 0; b < objSize; b += 4) Marshal.WriteInt32(o, b, 0);
                Marshal.WriteIntPtr(o, 0, pguid);
                Marshal.WriteInt32(o, P, ofs);
                Marshal.WriteInt32(o, P + 4, type);
                Marshal.WriteInt32(o, P + 8, flags);
                n++;
            };
            for (int i = 0; i < 6; i++) add(guidBlock + i * 16, i * 4, AXIS, ASPECTPOSITION);
            add(guidBlock + 6 * 16, 24, AXIS, ASPECTPOSITION);
            add(guidBlock + 6 * 16, 28, AXIS, ASPECTPOSITION);
            for (int i = 0; i < 4; i++) add(guidBlock + 7 * 16, 32 + i * 4, POV, 0);
            for (int i = 0; i < 32; i++) add(IntPtr.Zero, 48 + i, BUTTON, 0);

            dataFormat = Marshal.AllocHGlobal(DF_Size);
            for (int b = 0; b < DF_Size; b += 4) Marshal.WriteInt32(dataFormat, b, 0);
            Marshal.WriteInt32(dataFormat, 0, DF_Size);
            Marshal.WriteInt32(dataFormat, 4, objSize);
            Marshal.WriteInt32(dataFormat, 8, 1);
            Marshal.WriteInt32(dataFormat, 12, 80);
            Marshal.WriteInt32(dataFormat, 16, numObjs);
            Marshal.WriteIntPtr(dataFormat, DF_rgodf, objFormats);
        }

        void AllocEffectBuffers()
        {
            effectBuf = Marshal.AllocHGlobal(EFF_Size); // DIEFFECT (DX8)
            forceBuf = Marshal.AllocHGlobal(4);   // DICONSTANTFORCE
            axesBuf = Marshal.AllocHGlobal(4);
            dirBuf = Marshal.AllocHGlobal(4);
            Marshal.WriteInt32(axesBuf, 0, UseObjectIds ? axisObjectId : 0);    // X axis: object ID or DIJOFS_X
            Marshal.WriteInt32(dirBuf, 0, DirectionValue);
        }

        void FillConstant(int magnitude)
        {
            if (dirBuf != IntPtr.Zero) Marshal.WriteInt32(dirBuf, 0, DirectionValue);
            Marshal.WriteInt32(forceBuf, 0, magnitude);
            int[] e =
            {
                EFF_Size,                                 // dwSize
                DIEFF_CARTESIAN | (UseObjectIds ? 0x1 : DIEFF_OBJECTOFFSETS),    // dwFlags (0x1 = DIEFF_OBJECTIDS)
                EffectDurationUs,                         // dwDuration
                0,                                        // dwSamplePeriod
                10000,                                    // dwGain
                DIEB_NOTRIGGER,                           // dwTriggerButton
                0,                                        // dwTriggerRepeatInterval
                1,                                        // cAxes
            };
            for (int i = 0; i < e.Length; i++) Marshal.WriteInt32(effectBuf, i * 4, e[i]);
            for (int b = 32; b < EFF_Size; b += 4) Marshal.WriteInt32(effectBuf, b, 0);
            Marshal.WriteIntPtr(effectBuf, EFF_rgdwAxes, axesBuf);
            Marshal.WriteIntPtr(effectBuf, EFF_rglDirection, dirBuf);
            Marshal.WriteIntPtr(effectBuf, EFF_lpEnvelope, IntPtr.Zero);
            Marshal.WriteInt32(effectBuf, EFF_cbTSP, 4);
            Marshal.WriteIntPtr(effectBuf, EFF_lpvTSP, forceBuf);
            Marshal.WriteInt32(effectBuf, EFF_dwStartDelay, 0);
        }

        // ------------------------------------------------------------ per-frame update
        // force: -1..1 along the steering axis (positive = towards the axis maximum)
        public void SetForce(float force)
        {
            if (!Ready) return;
            if (Environment.TickCount < kickUntil) force = kickMagnitude / 10000f;

            // Measured 2026-11-26 on the T248: a positive DirectInput magnitude turns the wheel towards the
            // axis MINIMUM, so the sign is flipped here to keep "positive = towards the axis maximum".
            int m = -(int)(force * 10000f);
            if (m > 10000) m = 10000;
            if (m < -10000) m = -10000;
            try
            {
                // like ikt's mod: poll every update, grab the wheel again if it was taken away
                int hp = Vt<NoArgFn>(dev, DEV_Poll)(dev);
                if (hp < 0)
                {
                    reacquires++;
                    int ha = Vt<NoArgFn>(dev, DEV_Acquire)(dev);
                    if (ha >= 0) Vt<StartFn>(constant, EFF_Start)(constant, 1, 0);
                    else hrSet = ha;
                }
                if (ReadStateEachUpdate) ReadState();
                if (m == lastMagnitude && !RestartEachUpdate) return;   // nothing changed
                FillConstant(m);
                int flags = DIEP_TYPESPECIFICPARAMS;
                if (SendDirectionEachUpdate) flags |= DIEP_DIRECTION;
                if (RestartEachUpdate) flags |= DIEP_START;
                int hr = Vt<SetParamsFn>(constant, EFF_SetParameters)(constant, effectBuf, flags);
                if (hr != 0) hrSet = hr;
                updates++;
                lastMagnitude = m;
            }
            catch (Exception ex)
            {
                Status = "update error: " + ex.Message;
                Ready = false;
            }
        }

        // Test: push the wheel for about a second
        public string Kick(int magnitude)
        {
            if (!Ready) return "force feedback not running: " + Status;
            kickMagnitude = magnitude;
            kickUntil = Environment.TickCount + 1000;
            SetForce(0f);
            return "kick sent (" + magnitude + "), last code " + hrSet.ToString("X8");
        }

        // Force feedback state of the whole device, as text
        public string DeviceState()
        {
            if (dev == IntPtr.Zero) return "no device";
            int st;
            int hr = Vt<GetFfStateFn>(dev, 21)(dev, out st);
            if (hr != 0) return "GetForceFeedbackState failed " + hr.ToString("X8");
            string t = "";
            if ((st & 0x01) != 0) t += "EMPTY ";
            if ((st & 0x02) != 0) t += "STOPPED ";
            if ((st & 0x04) != 0) t += "PAUSED ";
            if ((st & 0x10) != 0) t += "ACTUATORS-ON ";
            if ((st & 0x20) != 0) t += "ACTUATORS-OFF ";
            if ((st & 0x40) != 0) t += "POWER-ON ";
            if ((st & 0x80) != 0) t += "POWER-OFF ";
            if ((st & 0x100) != 0) t += "SAFETY-SWITCH-ON ";
            if ((st & 0x200) != 0) t += "SAFETY-SWITCH-OFF ";
            if ((st & 0x400) != 0) t += "USER-FF-SWITCH-ON ";
            if ((st & 0x800) != 0) t += "USER-FF-SWITCH-OFF ";
            if ((st & 0x80000000) != 0) t += "DEVICE-LOST ";
            return string.Format("0x{0:X8} {1}", st, t);
        }

        public string EffectStatus()
        {
            if (constant == IntPtr.Zero) return "no effect";
            int st;
            int hr = Vt<GetEffStatusFn>(constant, 9)(constant, out st);
            if (hr != 0) return "GetEffectStatus failed " + hr.ToString("X8");
            return (st & 1) != 0 ? "PLAYING" : "not playing";
        }

        // ---------- raw helpers for the standalone test tool ----------
        IntPtr stateBuf;
        public bool ReadStateEachUpdate = false;

        // IDirectInputDevice8::GetDeviceState, like games do every frame
        public int ReadState()
        {
            if (dev == IntPtr.Zero) return -1;
            if (stateBuf == IntPtr.Zero) stateBuf = Marshal.AllocHGlobal(80);
            return Vt<GetDeviceStateFn>(dev, 9)(dev, 80, stateBuf);
        }

        public int Command(int cmd)
        {
            if (dev == IntPtr.Zero) return -1;
            return Vt<SendCmdFn>(dev, DEV_SendFFCommand)(dev, cmd);
        }

        // Create + start any effect with the given type-specific block. Returns the effect (or Zero) and the HRESULTs.
        public IntPtr PlayRaw(Guid type, byte[] typeSpecific, int durationUs, out int hrCreateOut, out int hrStartOut)
        {
            hrCreateOut = -1; hrStartOut = -1;
            if (dev == IntPtr.Zero) return IntPtr.Zero;
            IntPtr tsp = Marshal.AllocHGlobal(typeSpecific.Length);
            Marshal.Copy(typeSpecific, 0, tsp, typeSpecific.Length);
            FillConstant(0);
            Marshal.WriteInt32(effectBuf, 8, durationUs);
            Marshal.WriteInt32(effectBuf, EFF_cbTSP, typeSpecific.Length);
            Marshal.WriteIntPtr(effectBuf, EFF_lpvTSP, tsp);
            IntPtr eff;
            Guid g = type;
            hrCreateOut = Vt<CreateEffectFn>(dev, DEV_CreateEffect)(dev, ref g, effectBuf, out eff, IntPtr.Zero);
            Marshal.WriteIntPtr(effectBuf, EFF_lpvTSP, forceBuf);
            Marshal.WriteInt32(effectBuf, EFF_cbTSP, 4);
            Marshal.FreeHGlobal(tsp);
            if (hrCreateOut != 0) return IntPtr.Zero;
            hrStartOut = Vt<StartFn>(eff, EFF_Start)(eff, 1, 0);
            return eff;
        }

        public void StopRaw(ref IntPtr eff)
        {
            if (eff == IntPtr.Zero) return;
            try { Vt<NoArgFn>(eff, EFF_Stop)(eff); } catch { }
            Release(ref eff);
        }

        public void Relax()
        {
            SetForce(0f);
        }

        // ------------------------------------------------------------ cleanup
        static void Release(ref IntPtr obj)
        {
            if (obj == IntPtr.Zero) return;
            try { Vt<ReleaseFn>(obj, 2)(obj); } catch { }
            obj = IntPtr.Zero;
        }

        static void Free(ref IntPtr p)
        {
            if (p == IntPtr.Zero) return;
            Marshal.FreeHGlobal(p);
            p = IntPtr.Zero;
        }

        public void Dispose()
        {
            Ready = false;
            lastMagnitude = int.MinValue;
            if (constant != IntPtr.Zero)
            {
                try { Vt<NoArgFn>(constant, EFF_Stop)(constant); } catch { }
            }
            Release(ref constant);
            if (dev != IntPtr.Zero)
            {
                try
                {
                    Vt<SendCmdFn>(dev, DEV_SendFFCommand)(dev, DISFFC_STOPALL);
                    Vt<SendCmdFn>(dev, DEV_SendFFCommand)(dev, DISFFC_RESET);
                    Vt<NoArgFn>(dev, DEV_Unacquire)(dev);
                }
                catch { }
            }
            Release(ref dev); Release(ref di);
            Free(ref dataFormat); Free(ref objFormats); Free(ref guidBlock);
            Free(ref effectBuf); Free(ref forceBuf); Free(ref axesBuf); Free(ref dirBuf); Free(ref stateBuf);
        }
    }
}

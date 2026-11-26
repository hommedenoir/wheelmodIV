// GTA IV 1.0.7.0: switches off the game's own automatic up/down shifting, so the gear the mod sets
// is the gear the car drives in. Found 2026-11-26 in GTAIV.exe:
//   0xC7EC80 = transmission gear selection (transmission object = vehicle + 0x10E0:
//              +0x00 gear (word, 0 = reverse), +0x02 flags, +0x04 revs, +0x10 clutch, +0x14 throttle)
//   0xC7EEB9  jge -> jmp   : never shift up
//   0xC7EF01  jle -> jmp   : never shift down
// Forward/reverse selection is left alone. The original bytes are checked first, so a different
// game version is never patched, and everything is restored on Restore().
using System;
using System.Runtime.InteropServices;

namespace WheelMod
{
    public static class GearboxPatch
    {
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(IntPtr name);
        [DllImport("kernel32.dll")] static extern bool VirtualProtect(IntPtr addr, UIntPtr size, uint newProt, out uint oldProt);

        const uint PAGE_EXECUTE_READWRITE = 0x40;
        const int ImageBase = 0x400000;

        static readonly int[] Addr = { 0xC7EEB9, 0xC7EF01 };
        static readonly byte[][] Orig = {
            new byte[] { 0x0F, 0x8D, 0x84, 0x00, 0x00, 0x00 },   // jge 0xC7EF43
            new byte[] { 0x7E, 0x40 } };                          // jle 0xC7EF43
        static readonly byte[][] Patch = {
            new byte[] { 0xE9, 0x85, 0x00, 0x00, 0x00, 0x90 },   // jmp 0xC7EF43 ; nop
            new byte[] { 0xEB, 0x40 } };                          // jmp 0xC7EF43

        public static bool Active { get; private set; }
        public static string Status = "not applied";

        static IntPtr At(int va)
        {
            long b = GetModuleHandle(IntPtr.Zero).ToInt64();
            return new IntPtr(b + (va - ImageBase));
        }

        static bool Same(IntPtr p, byte[] want)
        {
            for (int i = 0; i < want.Length; i++) if (Marshal.ReadByte(p, i) != want[i]) return false;
            return true;
        }

        static void Write(IntPtr p, byte[] bytes)
        {
            uint old;
            VirtualProtect(p, new UIntPtr((uint)bytes.Length), PAGE_EXECUTE_READWRITE, out old);
            Marshal.Copy(bytes, 0, p, bytes.Length);
            uint dummy;
            VirtualProtect(p, new UIntPtr((uint)bytes.Length), old, out dummy);
        }

        public static bool Apply()
        {
            if (Active) return true;
            try
            {
                for (int i = 0; i < Addr.Length; i++)
                {
                    IntPtr p = At(Addr[i]);
                    if (!Same(p, Orig[i]) && !Same(p, Patch[i]))
                    {
                        Status = string.Format("NOT applied: unexpected bytes at 0x{0:X} (different game version?)", Addr[i]);
                        return false;
                    }
                }
                for (int i = 0; i < Addr.Length; i++) Write(At(Addr[i]), Patch[i]);
                Active = true;
                Status = "applied (game no longer shifts by itself)";
                return true;
            }
            catch (Exception ex) { Status = "error: " + ex.Message; return false; }
        }

        public static void Restore()
        {
            if (!Active) return;
            try
            {
                for (int i = 0; i < Addr.Length; i++) Write(At(Addr[i]), Orig[i]);
                Active = false;
                Status = "restored (game shifts by itself again)";
            }
            catch (Exception ex) { Status = "restore error: " + ex.Message; }
        }
    }
}

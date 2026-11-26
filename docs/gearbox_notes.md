# GTA IV gearbox - findings (2026-11-26)

GTAIV.exe 1.0.7.0 (image base 0x400000, ASLR possible -> mod uses GetModuleHandle).

Transmission object = vehicle + 0x10E0 (IVVehicle field names in brackets):
- +0x00 word  current gear, 0 = reverse            (CurrentGear)
- +0x02 word  shift flags (NOT "next gear")         (_f10e2)
- +0x04 float revs 0..1                             (EngineRPM)
- +0x08 float previous revs                         (_f10e8)
- +0x10 float CLUTCH 0..1                           (_f10f0)
- +0x14 float throttle copy                         (_f10f4)
- +0x18 dword time of the last shift

Functions:
- 0xC7F9D0  transmission process (per frame)
- 0xC7EC80  gear selection = GTA's automatic. Upshift writes gear+1 at 0xC7EEE1,
            downshift gear-1 at 0xC7EF2D. Each shift sets clutch = 0.1, which then rises by
            0.6 or 1.3 per second (handling dependent) up to 1.0.
- 0xC7EF50  drive force. RULE: if clutch < 0.6 and gear > 1 -> throttle forced to 0.
            1st gear is exempt. Force is multiplied by the clutch.

Why the mod's gearbox felt broken (builds 3a-3l): the mod forced a gear every frame, GTA's
automatic disagreed and "shifted" every frame, which dropped the clutch to 0.1 every frame,
which cut the throttle in every gear above 1st. TorqueFill/ForceClutch/AntiStall were
workarounds for that symptom and are now off by default.

Fix (build 3m, src/GearboxPatch.cs, applied in memory at runtime, GTAIV.exe on disk untouched):
- 0xC7EEB9  0F 8D 84 00 00 00 (jge) -> E9 85 00 00 00 90 (jmp)   never shift up
- 0xC7EF01  7E 40 (jle)             -> EB 40 (jmp)                never shift down
Original bytes are checked before patching; restored when switching to Automatic or on exit.
Clutch pedal now limits the game's real clutch (+0x10). Player shifts drop the clutch to 0.1
like GTA's own shifts.

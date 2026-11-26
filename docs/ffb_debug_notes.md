# Force feedback debugging - notes (night of 25/26 Nov 2026)

## Status
- Driving with the wheel works (steering, throttle, brake, per-car steering ratio).
- Force feedback does **not** reach the wheel yet - not in GTA IV, and not in the
  standalone test either. So the problem is in the mod's DirectInput code or in how the
  Thrustmaster driver treats it, not in GTA IV itself.

## What was found
| Check | Result |
|---|---|
| Device DirectInput sees | One FFB device: "Thrustmaster Advanced Mode Racer" (VID 044F, PID B696) - correct |
| Steering axis | X-Axis (Wheel), marked FF-ACTUATOR and FF-capable - correct |
| All DirectInput calls (create, start, update, acquire) | Return OK (00000000) |
| Effect status | Windows says PLAYING |
| Wheel state reported by the driver | `0x811` = **EMPTY + ACTUATORS-ON + USER-FF-SWITCH-OFF** - the wheel says it holds no effects and its "user FF switch" is off |
| Upload (Download) of the effect | returns 00000001 ("nothing to do") |
| 32-bit vs 64-bit | Identical - so not a 32-bit driver problem |
| Background vs foreground access, axis by offset vs by ID | Identical |
| Several effects at once (spring + damper + soft lock) | Third effect failed with 80040201 "device full" - switched to one constant-force effect |
| Thrustmaster panel "Test Forces" | Wheel moves - hardware is fine |
| Assetto Corsa | Force feedback works (strong, with Constant at 100 %) |
| Thrustmaster panel gains | Were Constant 10 %, Periodic 15 %, Spring 18 %, Damper 20 %; set to 100 % for testing. **Set Constant back to 10 % for Assetto Corsa.** |

## Research
- Microsoft: `DIGFFS_USERFFSWITCHOFF` / `ON` are only reported if the device can report a
  user FF switch; `DIGFFS_EMPTY` = no downloaded effects.
- GTR 2 (a 32-bit game from 2006) is known to work with T248 force feedback; guides say to
  press Alt-F in GTR 2 to reset force feedback once if it doesn't start. So 32-bit DirectInput
  force feedback does work with this wheel in general. GTR 2 on this PC would be a good
  cross-check: if its force feedback works, DirectInput is fine and only my code is off.
- ikt's Manual Transmission (GTA V) does exactly what the mod does: one constant-force
  effect, DIEFF_CARTESIAN | DIEFF_OBJECTOFFSETS, updated every frame with
  DIEP_DIRECTION | DIEP_TYPESPECIFICPARAMS | DIEP_START, and Poll/Acquire every frame.
  One difference left: ikt also reads the device state (GetDeviceState) every frame.

## Next test (ready to run)
`tools\FfbTest\run_both.bat` - builds and runs the **self-measuring** test, 32-bit and 64-bit.
Keep your hands OFF the wheel. It measures by itself (through the steering axis) whether
the wheel turned, and saves `FfbTest_results_32bit.txt` and `FfbTest_results_64bit.txt`.
Claude can read those files directly, no screenshot needed.

Variants it tries:
1. Constant force updated every frame + reading the device state every frame (like ikt)
2. Constant force, fixed 2.5 s, started once (the classic "hello world" of force feedback)
3. Same, after a full reset + actuators on + continue
4. A spring that should pull the wheel to about 25 % right
5. A strong sine vibration

If **any** variant moves the wheel, we know which way works and the mod gets changed to match.
If **none** does, the next suspects are the Thrustmaster driver/firmware version and how it
treats DirectInput from programs other than its own panel (then: driver reinstall, GTR 2 cross-check).

---------------------------------------------------------------------------
## SOLVED 2026-11-26 (night) - why the wheel never moved

Two bugs in ForceFeedback.cs, both found with tools\FfbTest (run_both.bat, run_test2.bat):

1. Wrong flag: DIEP_TYPESPECIFICPARAMS was 0x200 (that is DIEP_STARTDELAY). Correct = 0x100.
   Every "change the force" call changed the start delay instead, and returned OK.
   Effects that were CREATED with a force (test variants 2-5, A) always worked.
2. Direction: on the T248 a positive DirectInput magnitude turns the wheel to the LEFT
   (towards the axis minimum). SetForce now flips the sign, so in the mod positive = right.

Also changed: the effect is started once; updates only send the new strength (no restart).
Confirmed in GTA IV: `wheelmod_ffbtest 10000` turns the wheel to the right.

## Milestone 3 - car effects (written 2026-11-26, NOT yet tested in game)

New force parts, each shown as its own bar in the F6 window:
- Tyres (SAT): pulls the front wheels towards where the front axle is travelling.
  Builds weight in corners, goes LIGHT when the front tyres slide (understeer),
  and helps counter-steer when the rear slides. Goes light when the front wheels are in the air.
- Spring: small fixed centering (mainly when slow / standing).
- Damper: heaviness, strongest when standing still.
- Bumps: from quick changes in body roll.
- Crash: short jolt when the car hits something (impact above CollisionG, or a health drop).
- Lock: wall at the car's full steering lock.

Data used: GET_CAR_SPEED_VECTOR (world), GET_CAR_FORWARD_VECTOR, GET_CAR_HEADING (yaw rate),
GET_CAR_ROLL, GET_CAR_HEALTH, wheel flags NotInAir/IsRearWheel, Handling.TractionCurveLateral.

### Test steps
1. Run src\build.bat (must say Build OK), start GTA IV.
2. In WheelMod.ini set FfbGain=1 (it was 10 for testing - that makes everything max out),
   then `wheelmod_reload` in the F4 console.
3. Drive and watch the F6 window:
   - Driving straight: "sideways" should stay near 0 m/s. If it is big while going straight,
     the speed vector comes in the wrong frame - tell Claude.
   - "Front wheels on the ground" should be 2/2 on the road and drop when jumping.
   - In a corner the Tyres bar should pull against your steering; when the car understeers
     it should shrink again.
4. If the wheel pushes AWAY from centre at speed: tell Claude (do not just flip FfbInvert,
   the low-speed part was already confirmed correct).

### Tuning keys (WheelMod.ini, [ForceFeedback])
SatGain, SatFullSpeed, SatPeakDeg (0 = from handling), DetailGain, CollisionGain, CollisionG, AirMakesLight.

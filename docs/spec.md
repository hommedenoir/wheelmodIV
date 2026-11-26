# Wheel Mod - Spec (draft, 25 Nov 2026)

## Goal

Drive in GTA IV, TLAD and TBoGT with a wheel and pedals, including a manual gearbox
with clutch, while everything else keeps working the way GTA IV normally does.
Must work with **any** input device, not only the T248.

## 1. Mapping: every function mappable to anything

### Functions
- **Axes:** steering, throttle, brake, clutch, handbrake
- **Buttons:** shift up, shift down, H-shifter gears 1-7 and reverse, neutral,
  handbrake, horn, lights, look back, change camera, enter/exit vehicle,
  engine on/off, radio next/previous

### Sources
- any axis or button on any connected device (wheel, separate pedals, shifter,
  handbrake, button box, controller)
- keyboard keys
- devices are recognised by **VID/PID** (not by their number), so the order they
  were plugged in doesn't matter

### Per-axis settings
- invert (needed for T248 pedals)
- deadzone
- saturation (e.g. full brake at 90 % travel)
- curve / linearity
- combined throttle+brake on one axis (for wheels that do that)

### How mapping is done
- In-game window (IV-SDK .NET / ImGui) listing all functions.
- Click "Map", then move the pedal or press the button: it's detected and saved,
  the same way the input test works.
- Everything is stored in an `.ini` file: can also be edited by hand or shared.

### Profiles
- Several profiles, e.g. "T248 + controller", "controller only".
- Possibly separate settings for cars and bikes.

## 2. Default profile: wheel + controller + mouse

The default the user wants: play mainly with the controller, drive with the wheel,
and still use the mouse for drive-bys.

| Situation | What happens |
|---|---|
| **On foot** | Game untouched. Controller (and keyboard/mouse) work as normal. The mod does nothing. |
| **In a car** | Driving comes from the wheel: steering, throttle, brake, clutch, paddles. |
| | The controller's **driving** inputs (left stick, triggers) are muted so they can't fight the wheel. |
| | The controller's **other** buttons keep working: horn, radio, camera, exit vehicle. |
| | The **mouse** stays active for drive-bys exactly like in the original game: look with the mouse, click to shoot. The mod doesn't touch aiming or shooting. |
| **Switching** | Automatic: get in a car -> wheel takes over; get out -> everything back to the controller. |

Option: the wheel only takes over once it's actually moved, so a car can still be
driven with the controller when the wheel is switched off.

Open question: GTA IV switches its on-screen button hints depending on the last
input used. Mixing the mouse in may make them flicker between controller and
keyboard icons (cosmetic only). To be tested.

## 3. Gearbox modes

Gear handling as in IVWheelSupport (IV-SDK .NET can read and set the current gear).
Switchable in-game, saved per profile.

| Mode | How you shift | Clutch |
|---|---|---|
| **Sequential** (default for the T248) | Paddles: up/down one gear | Auto or manual (see assists) |
| **H-pattern** | Separate button per gear (H-shifter, or mapped buttons) | Manual or auto |
| **Automatic** | Game shifts by itself | none |
| **Automatic + manual override** | Game shifts, paddles force a gear for a moment | none |

### Assists (each on/off)
- **Auto-clutch:** the mod works the clutch for you when shifting, pulling away
  and stopping. You never stall and don't need the clutch pedal at all.
  If you do press the clutch pedal, it takes priority (so you can still do it yourself).
- **Auto-blip (rev matching):** on a downshift the mod gives a short throttle
  blip so the revs match the lower gear: smooth downshifts without a lurch.
- **Throttle cut on upshift:** briefly cuts power on an upshift, like a real
  sequential/paddle gearbox, so you can keep your foot down while shifting.
- **Anti-stall:** prevents stalling when releasing the clutch at too low RPM
  (only relevant when the clutch is done manually).

### Manual clutch behaviour (when auto-clutch is off)
- Cuts engine power while pressed.
- Stalls when released at too low RPM (unless anti-stall is on).
- Shifting without the clutch can be allowed or blocked (setting).

Default for the user's default profile: **Sequential + auto-clutch + auto-blip +
throttle cut** on the paddles, so no H-shifter or clutch work is needed.

## 3b. Lights, indicators, horn and siren

All mappable to any button (wheel, controller, keyboard) like everything else.

| Function | Behaviour |
|---|---|
| **Left indicator** | Toggle on/off. Turning the other indicator on switches this one off. |
| **Right indicator** | Same. |
| **Auto-cancel** (option) | Indicator switches itself off after the turn, based on the steering angle, like a real car. |
| **Hazard lights** | Toggle on/off, independent of the indicators. |
| **Headlights** | As in the game (off / low / high). |
| **Horn** | Horn **only**, as long as the button is held. |
| **Siren** | Separate button that toggles the siren on/off (emergency vehicles only). |

In the original game the horn and siren share one button (holding the horn
switches the siren). With the mod they're split: the horn button never triggers
the siren, and the siren has its own button.

Available in IV-SDK .NET: vehicle `LeftIndicator`, `RightIndicator`,
`HazardLights`, `SirenSwitchedOn`, and the natives `SET_VEH_INDICATORLIGHTS`,
`SET_VEH_HAZARDLIGHTS`, `SWITCH_CAR_SIREN`, `SOUND_CAR_HORN`.

## 4. Force feedback (later)

- Standard DirectInput effects so any FFB wheel works: centering spring, damper,
  road bumps, collisions, airborne.

## 5. Later / ideas
- Show speed, gear and RPM on the T248's built-in screen.
- TLAD/TBoGT: needs the episodes to run with IV-SDK .NET (EFLC is a separate exe in this install).
- Bikes need their own steering/shift handling.

## Order of work
1. ~~Input test, T248 layout~~ (done)
2. Mapping system + in-game mapping window
3. Steering, throttle, brake in the car (default profile)
4. Manual gearbox with clutch and paddles
5. Force feedback
6. T248 screen, episodes, bikes

## Later projects (added 2026-11-26)
- GTA III and GTA II: wheel + force feedback only (no gearbox work). GTA III has well-researched
  game code in the community, which should make it easier than IV. GTA II is top-down.
- Bike support (high priority once cars are done: The Lost and Damned is almost all bikes).
  Test: the bike chase missions in a normal playthrough.
- Real gearbox control: patch GTAIV.exe 1.0.7.0 so the game no longer shifts by itself or cuts the
  drive at idle revs (like ikt's GTA V Manual Transmission does). Replaces the TorqueFill workaround.
- Wheel buttons: D-pad up = hazards, left/right = indicators, down = headlights,
  a face button = answer phone, another = horn. Thumb buttons stay unused (some are worn).
- Linux test (Proton): needs an NVMe in a USB enclosure or a PCIe adapter as a test drive.
- Multiplayer: check whether GTA Connected and IV-SDK .NET run together.
- Feltzer hood camera is broken (game data, not the mod).

## Release checklist (added 2026-11-26)
Goal: other people can use the mod, not just the T248 setup.
1. Works for everyone: detect any wheel automatically (not only T248 VID/PID); test with at least
   one other wheel (Logitech); works without a clutch pedal or paddles.
2. Easy setup: in-game settings window to assign buttons and axes instead of editing WheelMod.ini.
3. Clean up: remove test lines in the HUD and the old workarounds (TorqueFill, ForceClutch,
   AntiStall, GearForceMode); sensible defaults.
4. Episodes and versions: test TLAD and TBoGT; the gearbox patch only knows GTAIV.exe 1.0.7.0 -
   other versions need their own addresses or a clear "not supported" message.
5. Readme: install steps, known limits, credits (IV-SDK .NET by ItsClonkAndre; ideas from ikt's
   GTA V Manual Transmission).
6. Release: GTAForums / gta4-mods / Nexus Mods, source code on GitHub.

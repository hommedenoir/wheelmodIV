# GTA IV Wheel Mod (working title)

Steering wheel, pedal, force feedback and manual-gearbox support for GTA IV
(The Lost and Damned and The Ballad of Gay Tony planned), working with any input device.
Built on IV-SDK .NET.

> **Work in progress.** This is the source code while the mod is being developed and tested.
> There is no packaged release yet, and so far it has only been tested with a Thrustmaster T248.
> Testers with other wheels are welcome: open an issue with your wheel model and what happened.

## What works so far
- Steering, throttle, brake and clutch from a wheel (tested: Thrustmaster T248)
- Per-car steering lock (the wheel's range adapts to each car)
- Force feedback: tyre force, centring, damping, bumps, crashes, soft lock
- Sequential gearbox on the paddles (R - N - 1 - 2 ...), clutch pedal optional
- GTA's own automatic shifting switched off in memory (GTAIV.exe 1.0.7.0 only, see docs/gearbox_notes.md)
- Small on-screen display: gear, speed, rev counter (hides itself for save prompts, phone, cutscenes, pause)
- Wheel buttons and D-pad mappable to actions, set up with an **in-game setup window** using only the wheel (F8):
  horn (the game's real horn key, so siren tap and yelp work), siren, hazard lights,
  left/right indicators, headlights (auto/on/off), interior light, phone, camera, get out,
  wheel on/off, or any keyboard key
- The wheel lets go of the car whenever the game takes control (death, arrest, cutscenes)

## Folders

| Folder | What's in it |
|---|---|
| `src\` | Source code of the mod |
| `tools\WheelInputTest\` | Guided input test: shows which axis/button each control uses |
| `tools\FfbTest\` | Self-measuring force feedback test (checks whether the wheel turns by itself) |
| `docs\` | Spec and plans, gearbox and force feedback findings, T248 mapping |

## Requirements
- GTA IV 1.0.7.0 (the gearbox patch checks the version and stays off on others)
- Ultimate ASI Loader
- IV-SDK .NET 1.8.1 or newer, tested up to 1.9.1 (console: F4)
- ClonksCodingLib.GTAIV

## How to build

1. Quit GTA IV (the game locks the script file while running).
2. Pass your game folder to the build (`src\build.bat "C:\path\to\GTAIV"`) or change the default at the top of `src\build.bat`.
3. Double-click `src\build.bat` and wait for **"Build OK"**.
4. Start the game. F6 = status window, F7 = wheel on/off, F8 = button setup. Settings: `IVSDKDotNet\scripts\WheelMod.ini`.

No Visual Studio needed: the build uses the C# compiler that comes with Windows.

## Credits
- IV-SDK .NET by ItsClonkAndre
- Force feedback ideas based on ikt's GTA V Manual Transmission

## Licence
[PolyForm Noncommercial 1.0.0](LICENSE.md): free to use, study, modify and share for non-commercial
purposes. Selling it or using it commercially is not allowed.

Copyright (c) 2026 hommedenoir. Not affiliated with Rockstar Games, Take-Two Interactive or Thrustmaster.
GTA IV game files are not included and never will be.

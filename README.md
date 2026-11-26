# GTA IV Wheel Mod (working title)

Steering wheel, pedal, force feedback and manual-gearbox support for GTA IV
(The Lost and Damned and The Ballad of Gay Tony planned), working with any input device.
Built on IV-SDK .NET. Work in progress, not released yet.

## What works so far
- Steering, throttle, brake and clutch from a wheel (tested: Thrustmaster T248)
- Per-car steering lock (the wheel's range adapts to each car)
- Force feedback: tyre force, centring, damping, bumps, crashes, soft lock
- Sequential gearbox on the paddles (R - N - 1 - 2 ...), clutch pedal optional
- GTA's own automatic shifting switched off in memory (GTAIV.exe 1.0.7.0 only, see docs/gearbox_notes.md)
- Small on-screen display: gear, speed, rev counter

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
- IV-SDK .NET 1.8.1 or newer (console: F4)
- ClonksCodingLib.GTAIV

## How to build

1. Quit GTA IV (the game locks the script file while running).
2. Edit the game path at the top of `src\build.bat` if needed.
3. Double-click `src\build.bat` and wait for **"Build OK"**.
4. Start the game. F6 = status window, F7 = wheel on/off. Settings: `IVSDKDotNet\scripts\WheelMod.ini`.

No Visual Studio needed: the build uses the C# compiler that comes with Windows.

## Credits
- IV-SDK .NET by ItsClonkAndre
- Force feedback ideas based on ikt's GTA V Manual Transmission

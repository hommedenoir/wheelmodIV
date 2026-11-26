# Thrustmaster T248 (PlayStation/PC version) - input layout

Measured with `tools\WheelInputTest` on 25 Nov 2026 (raw results: `T248_test_results_2026-11-25.txt`).

Device: VID **044F** (Thrustmaster), PID **B696**, 6 axes, 26 buttons.
Shows up as a plain DirectInput device, GTA IV itself does not see it.

| Control | Input | Released -> Pressed |
|---|---|---|
| Steering | axis **X** | center about 32,700; right -> about 64,000; left -> towards 0 |
| Throttle | axis **Y** | 65,535 -> 0 (reversed) |
| Brake | axis **Z** | 65,535 -> 0 (reversed) |
| Clutch | axis **R** | 65,535 -> 0 (reversed) |
| Shift up (right paddle) | button **2** | |
| Shift down (left paddle) | button **1** | |

All axis ranges are 0..65,535.

Notes:
- All three pedals are reversed: the mod has to invert them.
- The "SteerLeft = axis R" line in the raw results is a bug of the first test version
  (it took its starting snapshot before the wheel reported real values). Fixed in the tool.
- Not measured yet: the other wheel buttons, D-pad and the screen.

Other device connected during the test:
Device 1 = Microsoft/Xbox controller (VID 045E, PID 02FF), 5 axes, 16 buttons.

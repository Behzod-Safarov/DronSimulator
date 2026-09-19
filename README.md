# Drone Trainer

A PC flight simulator for teaching people to fly quadcopters, built in Unity (C#).
The flight model is a **5-inch freestyle FPV quad** (2306 motors, 5.1" tri-blade props, 6S Li-Po),
with a Betaflight-style flight controller (Angle / Horizon / Acro, Actual Rates, air mode),
procedural motor sound, an analog-style FPV camera feed, USB RC-transmitter input and a
scored lesson ladder.

## 1. Setup (first time)

1. Install **Unity Hub** and **Unity 6 (6000.x LTS)** — the project is pinned to 6000.3.24f1
   but any Unity 6 works (Hub offers to switch versions when opening).
2. Hub → **Projects → Add → Add project from disk** → this folder.
3. Open it. Menu **Drone Trainer → Build Training Scene** generates the field, drone, gates,
   cameras, lessons and every material/texture/terrain from code (no hand-made assets).
   If the console says the Input System backend was just enabled, restart the editor once.
4. **Drone Trainer → Play Training Scene** (or press Play).

Standalone build: File → Build Profiles → Windows → Build (the scene is already in the list).

## 2. Controls (Mode 2 layout)

| Action            | Keyboard       | Gamepad (Xbox)   | USB RC transmitter  |
|-------------------|----------------|------------------|---------------------|
| Throttle          | W / S          | Left stick ↑↓    | calibrated axis     |
| Yaw               | A / D          | Left stick ←→    | calibrated axis     |
| Pitch             | ↑ / ↓          | Right stick ↑↓   | calibrated axis     |
| Roll              | ← / →          | Right stick ←→   | calibrated axis     |
| Arm / disarm      | Space / Enter  | A                | button 1            |
| Cycle flight mode | M              | Y                | button 2            |
| Reset (fresh pack)| R              | B                | button 3            |
| Cycle camera      | C              | X                | button 4            |
| Drop payload      | F              | RB / RT          |                     |
| Pick scenario     | 1–9 (0 = free) |                  |                     |
| Next / retry lesson | N / Backspace | D-pad → / ←     |                     |
| Pause             | P / Esc        | Start            |                     |
| **Settings**      | **Tab**        | Back/Select      |                     |
| Axis debug panel  | F1             |                  |                     |

Arming needs the throttle at the bottom (a real quad hovers at ~35 %). Cameras: **Chase**
(learning), **Line of sight** (pilot standing at the pit, auto-zoom, Doppler audio), **FPV**
(on-board camera with lens distortion, video latency, noise/breakup by range and occlusion).

### Using a real RC transmitter (recommended)
Put the radio in *USB joystick / HID* mode, plug it in, press **Tab → Calibrate transmitter
sticks** and follow the four prompts (throttle up, yaw right, pitch forward, roll right). The
mapping is saved. Flying with the real radio is what makes sim hours transfer to the real drone.

## 3. Realism model

**Physics** ([DronePhysics.cs](Assets/Scripts/Drone/DronePhysics.cs))
- Per-motor: ESC command → target RPM limited by battery voltage → spool-up/down lag →
  thrust = kT·ω², drag torque = kQ·ω², prop-inertia reaction torque, gyroscopic coupling.
- Thrust falls with axial inflow (climbing / flying fast), rises in ground effect.
- Anisotropic body drag + rotor H-force drag, all relative to the wind.
- Wind: steady + Perlin gusts + turbulence + boundary layer ([Wind.cs](Assets/Scripts/Drone/Wind.cs)).
- Battery: Li-Po discharge curve, internal-resistance sag, capacity depletion → ~4 min hover,
  "LOW BATTERY" warning, motors die when empty (R = fresh pack). Infinite battery is a setting.

Defaults give T/W ≈ 8.7, hover at 34 % / 9,700 RPM / 9 A, ~26 A in a climb, 30° tilt at half stick in Angle mode.

**Flight controller** ([FlightController.cs](Assets/Scripts/Drone/FlightController.cs))
- Simulated gyro: noise, motor-vibration ripple, 150 Hz low-pass, one-step loop latency.
- Betaflight *Actual Rates* (centre sensitivity 70, max 670 deg/s, expo 0.54 — editable in Settings).
- Rate PIDs with D on measurement, air-mode mixer (throttle slides to keep authority), 5.5 % idle.
- Angle limit 60°, Horizon blend, Acro. `beginnerThrottleAssist` (stick centre = hover) exists
  for absolute beginners but is **off** by default because real quads don't do that.

**Sound** ([Assets/Scripts/Audio](Assets/Scripts/Audio)) — fully procedural: per-motor
blade-pass harmonics + electrical whine + RPM-shaped noise (the four motors beat against each
other), 3D with Doppler in line-of-sight view, airflow noise in FPV, impact thumps, arm/disarm beeps.

**Visuals**
- Drone: generated meshes ([MeshBuilder.cs](Assets/Editor/MeshBuilder.cs) — lathe, extrusion,
  tubes, twisted airfoil blades) assembled by [DroneModelBuilder.cs](Assets/Editor/DroneModelBuilder.cs):
  true-X carbon arms and plates with a procedural twill-weave normal map, anodised standoffs and
  bolts, FC/ESC stack with capacitor and wiring, brushed motor bells with cooling slots, tri-blade
  props (blur into discs at speed), battery + strap + XT60, TPU camera cage, tilted FPV camera,
  HD action cam, VTX lollipop and RX antennas, LED bar. All Standard-shader PBR.
- World: 1 km terrain with hills, **Poly Haven CC0 photo-scanned PBR layers** (graded turf,
  dry grass, dirt, gravel track around the gates), wind-animated detail grass, a PBR asphalt pit
  lane and a second asphalt lot with painted parking bays, procedurally grown trees
  ([TreeGenerator.cs](Assets/Editor/TreeGenerator.cs) — bark from Poly Haven, leaf-card crowns)
  in clumps plus a tree line at the horizon, bushes, pilot tent, safety net, race gates, hay
  bales, farm buildings with windows/doors/trim, sheds.
- Lighting: **Poly Haven HDRI sky** (`kloofendal_48d_partly_cloudy`) for skybox, ambient and
  reflections; the sun light is aimed at the HDRI's brightest texel automatically. Post-processing
  stack v2: ambient occlusion, bloom, ACES tone-mapping, motion blur, SMAA.
- FPV post-effect ([FpvAnalog.shader](Assets/Shaders/FpvAnalog.shader)): fisheye (auto-zoomed to
  fill any aspect ratio), chromatic fringe, grain, scanlines, vignette, tearing/snow when far from
  the pilot or behind a building, ~28 ms video latency, throttle-dependent jello.

External assets live in `Assets/External/PolyHaven` (CC0, see LICENSE.txt there); generated
meshes/textures in `Assets/Generated` (rebuilt by the scene builder, safe to delete).

## 4. Scenarios

Press a number key at any time (0 = free flight, the free-flight panel lists them):

| # | Scenario            | Mode  | Goal |
|---|---------------------|-------|------|
| 1 | Hover in the box    | Angle | Hold position inside the yellow 3×2×3 m box for 10 s |
| 2 | Gate course         | Angle | 6 race gates in order (green = next) |
| 3 | Precision landing   | Angle | Climb above 3 m, land on the H pad under 2 m/s, hold 2 s |
| 4 | Gate course in Acro | Acro  | Same course, no self-levelling, 2 laps |
| 5 | **Balloon pop**     | any   | 14 balloons on strings around the field at 1.5–11 m; fly through all of them, fastest time wins |
| 6 | **Water-balloon drop** | any | Carry 6 water balloons (0.12 kg each — the quad really gets heavier), press **F** over a bullseye; the balloon keeps your velocity and falls with drag, 10 / 5 / 2 points by ring |
| 7 | **Race – 3 laps**   | Acro  | Lap timer, last/best lap, record kept between sessions |
| 8 | **Autopilot agent** | —     | The drone flies the mission itself (take-off, gate loop at 4 m, return, land, disarm). Move a stick to take over; Backspace hands it back |
| 9 | **Kamikaze demolition** | any | Three exact target cars (modelled sedan: body, cabin, raked windshield/rear window, mirrors, door trim, licence plates, exhaust, alloy wheels with lug nuts, breakaway bumpers) parked in a real asphalt lot with painted bays inside the gate loop, each marked with a spinning orange diamond — plus crate towers, barrel pyramids and block walls further out. Fly into anything and the drone goes off like a firework: car windows shatter, wheels and bumpers pop off on breakable joints and tumble away independently, a scorch mark and drifting smoke are left on the ground, and nearby structures blast apart too (physics chain-reaction — hitting one car can topple its neighbour). 5 drones per run, score = % demolished (each target car counts for 8 points, worth more than a crate) |

Score = 100 − 15 per crash − 0.5 per second over par. Add scenarios by deriving from `Lesson`
and placing the component under `Trainer/Lessons` (see `SceneBuilder.BuildTrainer`). The agent is
[Autopilot.cs](Assets/Scripts/Training/Autopilot.cs): a position → velocity → tilt controller that
drives the same stick interface as the pilot, with a wind integral and yaw-to-track.

## 5. Tuning

Everything is on the `Drone` and `Trainer` objects in the Inspector (live in Play mode) or in the
in-game **Settings (Tab)** panel: rates, angle limit, throttle assist, air mode, battery, ground
effect, wind, FPV latency/distortion/FOV, volume. Settings persist between runs.

If it wobbles, lower the rate PID `p`/`d`; if it feels mushy, raise `p`. A docile trainer:
`motorKv 1750 → 1400` or `loadedSpeedFraction 0.65 → 0.5` (lower T/W).

## 6. Automated checks

- `Unity.exe -projectPath <dir> -executeMethod SceneBuilder.BuildAndPlay -droneSelfTest`
  rebuilds the scene, enters Play mode and flies a scripted sequence, logging `[SelfTest] PASS/FAIL`.
- `-droneRemote <cmd.txt>` lets a script drive the sim (camera, sticks, screenshots) — see
  [DevRemote.cs](Assets/Scripts/Training/DevRemote.cs).

## 7. Project layout

```
Assets/
  Editor/SceneBuilder.cs                          one-click scene generator (sky, terrain, trees, course, post-fx)
  Editor/DroneModelBuilder.cs, MeshBuilder.cs     drone model from generated meshes
  Editor/CarBuilder.cs                            exact target-car model (body, glass, lights, breakaway wheels)
  Editor/TreeGenerator.cs, ProceduralTextures.cs  trees, carbon weave / leaf / grass textures
  Editor/GeneratedAssets.cs                       texture import rules, PBR material helpers
  External/PolyHaven/                             CC0 HDRI + PBR texture sets (downloaded)
  Shaders/FpvAnalog.shader                        FPV camera post effect
  Scripts/
    SimBootstrap.cs                               physics rate, quality
    Drone/   DronePhysics, FlightController, Pid, Wind, PropVisuals, CrashDetector, RigidbodyCompat
    Input/   PilotInput (joystick/gamepad/keyboard), StickCalibrator (wizard)
    Audio/   DroneAudio, WindAudio, ImpactAudio (all synthesised)
    Cameras/ ChaseCamera, LineOfSightCamera, CameraSwitcher, FpvCameraEffects
    Training/ Lesson, HoverBoxLesson, GateCourseLesson, LandingLesson, Gate, LessonManager,
              AutoPilotSelfTest, DevRemote
    UI/Osd.cs                                     telemetry, lesson panel, sticks, settings
  Scenes/Training.unity, Materials/               generated
```

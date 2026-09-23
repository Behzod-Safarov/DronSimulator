# Drone Trainer

A PC flight simulator for teaching people to fly quadcopters, built in Unity (C#).
The flight model is a **5-inch freestyle FPV quad** (2306 motors, 5.1" tri-blade props, 6S Li-Po),
with a Betaflight-style flight controller (Angle / Horizon / Acro, Actual Rates, air mode),
procedural motor sound, an analog-style FPV camera feed, real RC-transmitter input over USB or
UART, and a scored lesson ladder.

## 1. Setup (first time)

1. Install **Unity Hub** and **Unity 6 (6000.x LTS)** — the project is pinned to 6000.3.24f1
   but any Unity 6 works (Hub offers to switch versions when opening).
2. Hub → **Projects → Add → Add project from disk** → this folder.
3. Open it. Menu **Drone Trainer → Build Training Scene** generates the field, drone, gates,
   cameras, lessons and every material/texture/terrain from code (no hand-made assets).
   If the console says the Input System backend was just enabled, restart the editor once.
4. **Drone Trainer → Play Training Scene** (or press Play).

Standalone build: File → Build Profiles → Windows → Build (the scene is already in the list).

Serial input needs the **.NET Framework** API compatibility level (Project Settings → Player);
*Build Training Scene* sets it, because `System.IO.Ports` does not exist under .NET Standard.

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
| Pick discipline   | 1–5 (0 = menu) |                  |                     |
| Next drill / retry | N / Backspace |                  |                     |
| Pause             | P / Esc        | Start            |                     |
| **Settings**      | **Tab**        | Back/Select      |                     |
| Axis debug panel  | F1             |                  |                     |

Arming needs the throttle at the bottom (a real quad hovers at ~35 %). Cameras: **Chase**
(learning), **Line of sight** (pilot standing at the pit, auto-zoom, Doppler audio), **FPV**
(on-board camera with lens distortion, video latency, noise/breakup by range and occlusion).

### Using a real RC transmitter over USB (HID)
Put the radio in *USB joystick / HID* mode, plug it in, press **Tab → Calibrate transmitter
sticks** and follow the four prompts (throttle up, yaw right, pitch forward, roll right). The
mapping is saved. Flying with the real radio is what makes sim hours transfer to the real drone.

### Using a real RC transmitter over UART (serial)
A radio with no HID mode still speaks its link protocol out of a serial pin, and the trainer
reads that stream directly - the same bytes a flight controller sees. Wire the transmitter
module's (or receiver's) **TX** to a USB-TTL adapter's **RX**, join the **grounds**, and leave
the rest alone. Then **Tab → Pult UART (serial)**: pick the **Port** and **Baud**, choose the
protocol, press **Connect**. The CH1-CH6 bars move with the sticks, which is the quickest way
to see whether the channel order is what you think it is.

| Protocol | Frame on the wire | Baud | Comes from |
|----------|-------------------|------|------------|
| `Crsf`   | `0xC8 len 0x16 <16 x 11 bits> crc8` | 420000 8N1 | ELRS / TBS Crossfire modules and receivers |
| `Text`   | `$RC,1500,1500,1000,1500,1000,1000\n` (microseconds) | 115200 | Arduino / Pico / anything you write yourself |
| `Auto`   | whichever turns up first | - | leave it here unless frames are being misread |

Channels are **1 roll, 2 pitch, 3 throttle, 4 yaw, 5 arm, 6 mode** (Angle / Horizon / Acro at
1000 / 1500 / 2000 us), all remappable in [SerialRc.cs](Assets/Scripts/Input/SerialRc.cs).
While frames are arriving the UART outranks the gamepad and the keyboard, and the OSD shows
`IN UART COM4 50 Hz`.

Half a second of silence is a lost link: the sticks go neutral, the HUD says so, and a second
later the failsafe cuts the motors - the same procedure [RcLink.cs](Assets/Scripts/Drone/RcLink.cs)
runs at the edge of RF range. When the link comes back the arm switch has to be flipped off and
on again before the motors will spin, which is the rule Betaflight enforces for the same reason.

### Build your own remote (ESP32-S3 + two thumbsticks)
[Tools/RemoteEsp32](Tools/RemoteEsp32) is firmware for a hand-built transmitter: an ESP32-S3, two
analog thumbsticks, nothing else. It sends the text format at 50 Hz (or CRSF with one `#define`),
learns each stick's real travel into flash, treats the spring-return left stick as a throttle
*rate* so the setting holds when you let go, and uses the stick buttons as the arm and mode
switches. Wiring, board settings and the calibration ritual are in
[its README](Tools/RemoteEsp32/README.md).

### Transmitter simulator (no radio needed)
[Tools/RemoteSim](Tools/RemoteSim) is a small console app that pretends to be the radio and
streams control data down a UART, so the serial path can be flown and tested with no hardware:

A built copy ships next to the game: **`Build\RemoteSim\RemoteSim.exe`** (self-contained, no
.NET install needed) with `remote-sim.bat` beside it, which asks for the port and then hands you
the sticks. From source:

```
cd Tools\RemoteSim
dotnet run -- --port COM12                     keyboard sticks, CRSF at 50 Hz
dotnet run -- --port COM12 --format text       same frames, readable in a terminal
dotnet run -- --port COM12 --pattern circle    hands-off: arms and flies a circle
dotnet run -- --file frames.bin --pattern checkout --seconds 12    capture to a file
dotnet run -- --list                           serial ports on this machine
```

Keys are W/S throttle, A/D yaw, arrows pitch/roll, SPACE arm switch, M mode switch, Q quit.
`--pattern checkout` is a full systems check on a twelve-second loop: it arms with the throttle
down, flies every stick, steps through all three flight modes and disarms again.

A UART is a cable, so it needs two ends: either two USB-TTL adapters with TX-RX crossed and the
grounds joined (simulator on `COM3`, trainer on `COM4`), or a virtual pair such as
[com0com](https://sourceforge.net/projects/com0com/) when there is no hardware at all. Neither
is needed just to check the decoder - a captured file can be replayed straight into a running
sim, see §6.

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
- **Views**: chase, line of sight and the drone.s own FPV camera. The main view is changed from
  the screen - **click a button in the bottom-left row**, press **1 / 2 / 3**, or **C** to cycle.
- **Second view inset**: whichever view is not the main one renders into a corner monitor
  ([FpvMonitor.cs](Assets/Scripts/Cameras/FpvMonitor.cs)) - so the drone camera is watchable while
  you fly line-of-sight, the way a spotter watches a pilot.s feed. **Click the inset to swap it
  into the main view**, or **V** to hide it. If the main view takes over the camera the inset was
  showing, the inset falls back to another one instead of going blank.
- Horizon: a mountain ring beyond the tree line (terrain height range 170 m), so the field sits in
  a valley instead of ending flat against the sky.
- Ground cover: three detail types - lush tufts, bleached dry tufts and sparse flowering weeds -
  mixed by biome, plus field stones worked up through the meadow grass.
- FPV post-effect ([FpvAnalog.shader](Assets/Shaders/FpvAnalog.shader)): fisheye (auto-zoomed to
  fill any aspect ratio), chromatic fringe, grain, scanlines, vignette, tearing/snow when far from
  the pilot or behind a building, ~28 ms video latency, throttle-dependent jello.

External assets live in `Assets/External/PolyHaven` (CC0, see LICENSE.txt there); generated
meshes/textures in `Assets/Generated` (rebuilt by the scene builder, safe to delete).

## 4. Training program

The app opens on a **title screen** over a cinematic camera of the field, then a
**discipline picker** (six cards: five disciplines + free flight) and a **drill list** with a
briefing, scoring rules and your best result per drill ([MainMenu.cs](Assets/Scripts/UI/MainMenu.cs)).
Mouse, keyboard (arrows / WASD, Enter, Esc, 1-5, F) and gamepad (d-pad, A, B) all work.
The UI is in **Uzbek (Latin) by default**; press **L** on the title screen (or use Settings) for
English. Strings live in [Loc.cs](Assets/Scripts/UI/Loc.cs): a phrase table for menus and briefings, plus
regex phrases for in-flight statuses that contain numbers. Unknown strings fall through in English.
A discipline opens at your first drill without 3 stars. In flight **Esc** pauses (resume / retry /
next drill / choose drill / main menu); every finished drill shows a **results card** with animated
stars, the score counting up and "new personal best". Each drill starts in its suggested flight
mode (change with M). Every drill is scored 0–100 and rated 0–3 stars; best stars, best score
and attempts are saved between sessions and shown on the cards.

Each discipline has its own **venue** on the field ([VenueBuilder.cs](Assets/Editor/VenueBuilder.cs)); a drill
spawns you (and the line-of-sight pilot) at its venue:

Each venue also sits in **its own biome** - different ground, vegetation, terrain shape and
weather ([VenueBuilder.cs](Assets/Editor/VenueBuilder.cs), [VenueAtmosphere.cs](Assets/Scripts/World/VenueAtmosphere.cs)):

| Venue | Where | Landscape | Light & air | What is there |
|---|---|---|---|---|
| **Pit field** | origin | green meadow: mown turf, detail grass, wooded clumps, farm buildings, hay bales, a pond | midday, clear, thin haze | asphalt pad, tent, safety net, hover box, H landing pad, orbit pylon |
| **Race track** | 170 m east | desert plateau: sand, dunes, boulder fields, a mesa ridge of stacked slabs, dry scrub, no grass | low afternoon sun, warm dusty haze | gravel oval with 8 gates, start/finish arch with chequered banner, tyre walls, cones, flags, grandstand, timing tower |
| **Freestyle park** | 170 m west | urban wasteland: stone/gravel ground, rubble mounds, nine derelict towers with window bands, lamp posts, no vegetation | overcast, cool grey, dim sun | concrete slab with a 3-floor skeleton building to dive through, a window wall, two ramps, a row of pillars, stacked containers |
| **Demolition yard** | 185 m north | quarry basin: the yard is dug into terraced rock benches, boulders and conical spoil heaps | low evening sun, thick dust | fenced dirt yard with hazard-striped gate, jersey barriers, two warehouses, container stacks, floodlight masts, the target-car lot, crate towers, barrel pyramid, block walls, balloons and drop bullseyes |

Starting a drill eases the sun angle/colour/intensity, ambient level, fog and sky exposure to that
venue preset, so each discipline has its own time of day as well as its own ground.

| # | Discipline | Drills |
|---|---|---|
| 1 | **Basics** | Hover in the box · Precision landing · Orbit the pylon (2 laps, 3–8 m band, yaw-and-roll coordination) |
| 2 | **Racing** | Gate course · Gates in Acro (2 laps) · Race – 3 laps with a 3-2-1-GO countdown (false-start detection) and lap record · Time trial – best lap under 22 s |
| 3 | **Freestyle** | Rolls ×3 · Flips ×3 · Power loops ×2 — tricks are recognised from the quad's real body rates ([TrickDetector.cs](Assets/Scripts/Training/TrickDetector.cs)): a full 360° about the roll or pitch axis, and a flip that gains ≥ 2.5 m at the top counts as a power loop |
| 4 | **Demolition** (kamikaze game) | Balloon pop · Water-balloon drop · Hit the marked car (only the red car counts; fewer drones = more stars) · Speed hit (hit the blue car at ≥ 12 m/s) · Kamikaze demolition (60 % of everything with 5 drones) |
| 5 | **Agent** | Autopilot agent: the drone flies the loop itself; touch the sticks to take over |

### Every drill is briefed for a student
Selecting a drill shows a teaching card, not just a sentence ([MainMenu.cs](Assets/Scripts/UI/MainMenu.cs)):

- **Goal** - one line stating what must be achieved
- **Numbered steps** - the 4 actions to take, in order
- **You learn** - the skill it builds and why it matters on a real quad
- **Watch out** - the mistake students actually make here
- **Controls** - only the keys this drill needs
- **Difficulty** 1-5, plus the scoring rule and your best result

The goal is repeated on the launch screen and in the in-flight panel, so the student always
knows the task without leaving the air. All of it is translated (Uzbek by default).

Add a drill by deriving from `Lesson` and adding it under `Trainer/Lessons` with a `discipline`
name and a `Brief(...)` call (see `SceneBuilder.BuildTrainer`); consecutive drills with the same
name form a discipline.

### Consequences (realism that trains)
- **Prop strikes**: an impact between 2.4 and 5 m/s (clipping a gate, a hard landing) breaks one prop
  instead of crashing — you lose 55–75 % of that motor's thrust, the quad yaws and wobbles, and you
  have to land and press **R** for a new prop. Above 5 m/s it's a crash and auto-respawns.
- **Prop wash / vortex ring state**: descending faster than ~2.5 m/s with little forward speed puts
  the props in their own wake — thrust drops and the quad wobbles ("PROP WASH — add forward speed").
  Real quads do exactly this; the fix is the same.
- Battery sag, wind gusts and the payload's extra mass all still apply inside drills.
## 5. Tuning

Everything is on the `Drone` and `Trainer` objects in the Inspector (live in Play mode) or in the
in-game **Settings (Tab)** panel: rates, angle limit, throttle assist, air mode, battery, ground
effect, wind, FPV latency/distortion/FOV, volume. Settings persist between runs.

If it wobbles, lower the rate PID `p`/`d`; if it feels mushy, raise `p`. A docile trainer:
`motorKv 1750 → 1400` or `loadedSpeedFraction 0.65 → 0.5` (lower T/W).

## 6. Automated checks

- `Unity.exe -projectPath <dir> -executeMethod SceneBuilder.BuildAndPlay -droneSelfTest`
  rebuilds the scene, enters Play mode and flies a scripted sequence, logging `[SelfTest] PASS/FAIL`.
- `-droneRemote <cmd.txt>` lets a script drive the sim - see [DevRemote.cs](Assets/Scripts/Training/DevRemote.cs).
  Commands include `camera`, `sticks`, `shot`, `lesson N`, `menu`, `nav <key>`, `goto x y z`,
  `blocks` (demolition block state) and `log`, whose line carries altitude, arming, `ts=`
  (time scale), `ap=` (autopilot phase) and `menu=` (front-end state).
- `Unity.exe -projectPath <dir> -executeMethod SerialRcSelfTest.Run -rcCrsf <capture.bin> -rcText <capture.txt> -quit`
  runs both wire-format decoders over a round trip and over real captures, logging `[RcTest] PASS/FAIL`
  ([SerialRcSelfTest.cs](Assets/Editor/SerialRcSelfTest.cs)). The same check is on the editor menu as
  *Drone Trainer -> Test RC link parsers*.
- `rcfeed <capture.bin>`, `rcstop` and `rc` replay a captured transmitter stream into the serial link
  at 50 Hz with no port open at all, and print the decoded channels, so the whole UART path - sticks,
  arm switch, mode switch, dropout failsafe - can be driven from a script.

## 7. Project layout

```
Assets/
  Editor/SceneBuilder.cs                          one-click scene generator (sky, terrain, trees, course, post-fx)
  Editor/DroneModelBuilder.cs, MeshBuilder.cs     drone model from generated meshes
  Editor/CarBuilder.cs                            exact target-car model (body, glass, lights, breakaway wheels)
  Editor/TreeGenerator.cs, ProceduralTextures.cs  trees, carbon weave / leaf / grass textures
  Editor/GeneratedAssets.cs                       texture import rules, PBR material helpers
  Editor/SerialRcSelfTest.cs                      CRSF / text decoder checks (menu + batch mode)
  External/PolyHaven/                             CC0 HDRI + PBR texture sets (downloaded)
  Shaders/FpvAnalog.shader                        FPV camera post effect
  Scripts/
    SimBootstrap.cs                               physics rate, quality
    Drone/   DronePhysics, FlightController, Pid, Wind, PropVisuals, CrashDetector, RcLink (link quality + failsafe), RigidbodyCompat
    Input/   PilotInput (UART/joystick/gamepad/keyboard), SerialRc (transmitter over serial), StickCalibrator
    Audio/   DroneAudio, WindAudio, ImpactAudio (all synthesised)
    Cameras/ ChaseCamera, LineOfSightCamera, CameraSwitcher, FpvCameraEffects
    Training/ Lesson, HoverBoxLesson, GateCourseLesson, LandingLesson, Gate, LessonManager,
              AutoPilotSelfTest, DevRemote
    UI/MainMenu.cs                                title, discipline/drill menus, pause, results card
    UI/Osd.cs                                     telemetry, lesson panel, sticks, settings
    Cameras/ShowcaseCamera.cs                     cinematic orbits behind the menus
  Scenes/Training.unity, Materials/               generated
Tools/
  RemoteSim/                                      transmitter simulator: CRSF or text frames out of a UART
  RemoteEsp32/                                    ESP32-S3 + two thumbsticks remote firmware (Arduino)
```

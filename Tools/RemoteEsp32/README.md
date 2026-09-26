# ESP32-S3 remote

Firmware for a hand-built transmitter: an ESP32-S3 and two analog thumbsticks, streaming RC
channels down a UART in the format [SerialRc.cs](../../Assets/Scripts/Input/SerialRc.cs) decodes.
Mode 2, same as the simulator: **left stick = throttle + yaw, right stick = pitch + roll**.

Verified to compile clean against esp32 core 3.3.11 for `esp32:esp32:esp32s3`, in both output
formats (322 KB flash, 22 KB RAM).

## Parts

- ESP32-S3 board (DevKitC-1 or any S3 with a USB port)
- 2 analog thumbstick modules (KY-023 / PS2-style: VRx, VRy, SW, +, GND)
- Nothing else. No level shifters, no resistors.

## Wiring

| Stick | Signal | ESP32-S3 pin | Channel |
|---|---|---|---|
| Left | VRx | GPIO1 | throttle (3) - stick up/down |
| Left | VRy | GPIO2 | yaw (4) - stick left/right |
| Left | SW | GPIO5 | arm switch (5) |
| Right | VRx | GPIO3 | pitch (2) - stick up/down |
| Right | VRy | GPIO4 | roll (1) - stick left/right |
| Right | SW | GPIO6 | mode switch (6) |
| Both | + (often labelled **+5V**) | **3V3** | |
| Both | GND | GND | |

The channel column assumes the modules are mounted **turned 90°, pin header on the side**, as on
the reference remote: that way each module's VRx carries up/down. Mounted the other way round,
swap the pin numbers of each pair at the top of the sketch (`PIN_THROTTLE`/`PIN_YAW`,
`PIN_PITCH`/`PIN_ROLL`). A direction that comes out backwards is one `INVERT_*` flag.

Two things matter here:

- **3V3, not 5V.** The sticks are just potentiometers and work happily at 3.3 V; feeding 5 V into
  an ESP32 ADC pin damages it.
- **GPIO1–GPIO10 only** for the four axes. Those are ADC1. ADC2 pins stop reading the moment the
  WiFi radio is enabled, which is a miserable fault to find months later.

The buttons need no resistors — they go to ground and the internal pull-ups do the rest.

## Build and flash

Arduino IDE: open `RemoteEsp32.ino`, **Tools → Board → ESP32 Arduino → ESP32S3 Dev Module**, and
set **USB CDC On Boot: Enabled** so the board's own USB port becomes the serial port. Upload.

Or from the command line:

```
arduino-cli compile --fqbn esp32:esp32:esp32s3 Tools\RemoteEsp32
arduino-cli upload  --fqbn esp32:esp32:esp32s3 -p COM8 Tools\RemoteEsp32
```

## Calibrate the sticks once

Thumbsticks are never centred at 2048 and never travel the same distance both ways, so the
firmware learns each one:

1. Hold the **left stick's button** down and plug the board in (or press reset).
2. The LED blinks **yellow** for six seconds — sweep both sticks into all four corners, several
   times, right to the stops.
3. The LED turns **blue** — take your hands off and let the sticks centre for about a second.
4. Three **green** flashes: saved to flash. It is remembered across power cycles.

The LED afterwards: **blue** disarmed, **green** armed, slow blinking **red** means no calibration
has ever been stored (it still flies, just on default numbers), and a **fast red** blink after
pressing arm means arming was refused because the throttle stick was not idle.

The remote refuses to arm unless the throttle stick is left centred (or at the bottom, with a
real throttle). That is the "throttle low to arm" rule every flight controller has, and it also
catches an unpowered stick: a throttle pin at 0 V looks like the stick held fully up, and without
the check the quad would arm straight into full power.

## Connect it to the trainer

1. Close the Arduino **Serial Monitor** - only one program can hold a serial port at a time.
2. Plug the remote in and start Drone Trainer. It finds the remote by itself, on whatever COM
   number it landed on, and the HUD's bottom line shows `IN UART COM9 50 Hz`.
3. **Tab → Pult UART (serial)** shows the CH1-CH6 bars following the sticks.
4. Hands off the sticks, press the left stick's button to arm, throttle up.

A remote that has never been calibrated takes the sticks' resting position at power-up as their
centre, so leave the sticks alone while you plug it in. Calibrate properly when you get a chance.

## Throttle behaviour

A thumbstick springs back to the middle, and a throttle that returns to the middle when you let go
is not a throttle. So by default the left stick's Y axis is a throttle **rate**: push up to add
power, pull down to take it away, let go and the setting holds — like the ratcheted throttle on a
real radio. `THROTTLE_RATE` (700 µs/s) sets how fast it moves.

If you fit a real ratcheted stick or a plain potentiometer, set `THROTTLE_SPRING_RETURN` to 0 and
the axis maps straight to 1000–2000 µs.

Arming always resets the throttle to minimum, because the simulator — like any flight controller —
refuses to arm with the throttle up.

## CRSF instead of text

Set `USE_CRSF` to 1 and the board sends real ELRS/TBS frames (`0xC8 len 0x16`, 16 × 11 bits, CRC8)
at 420000 baud; pick **Crsf** and 420000 in the game. The text format is easier to read in a
terminal while building; CRSF is what real hardware speaks, so it is the honest end state.

## Feeding a separate UART instead of USB

Set `USE_NATIVE_USB` to 0 and the frames leave on **GPIO17** at the chosen baud, for a USB-TTL
adapter or a real flight controller's RX pin. Join the grounds.

## When it misbehaves

| Symptom | Cause |
|---|---|
| Game says the port is busy | Serial Monitor, PuTTY or another RemoteSim still has it open |
| No frames, port opens fine | Wrong baud, or `USE_CRSF` disagrees with the protocol picked in the game |
| A stick moves the wrong way | Flip the matching `INVERT_*` flag at the top of the sketch |
| Stick sits off-centre, or hits the end early | Run the calibration again |
| Throttle creeps up on its own | Stick centre drifted — recalibrate, or raise `DEADBAND` |
| Arms, then nothing | Throttle has to be at minimum to arm; it is reset on every arm |
| Arm button gives a fast **red** blink, won't arm | The throttle stick isn't idle - hands off it. If it keeps happening with your hands off, the throttle pin reads 0 V: no 3V3 on the stick module |
| "RC SIGNAL LOST" then a failsafe | Frames stopped for half a second — cable, USB port, or a reset |
| Bars pinned at 1000 / 2000 with the sticks centred | Pins read 0 V: set `DEBUG_RAW` to 1 and read the `# raw` lines - 0 means unwired or no 3V3 to the module |

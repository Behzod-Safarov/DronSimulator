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
| Left | VRx | GPIO1 | yaw (4) |
| Left | VRy | GPIO2 | throttle (3) |
| Left | SW | GPIO5 | arm switch (5) |
| Right | VRx | GPIO3 | roll (1) |
| Right | VRy | GPIO4 | pitch (2) |
| Right | SW | GPIO6 | mode switch (6) |
| Both | + | **3V3** | |
| Both | GND | GND | |

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

The LED afterwards: **blue** disarmed, **green** armed, blinking **red** means no calibration has
ever been stored (it still flies, just on default numbers).

## Connect it to the trainer

1. Close the Arduino **Serial Monitor**. Only one program can hold a serial port, and the monitor
   is the usual reason the game says the port is busy.
2. Start Drone Trainer, press **Tab → Pult UART (serial)**.
3. **Port** = the board's port, **Baud** 115200, protocol **Text** (or **Auto**), press **Ulanish**.
4. The CH1–CH6 bars move with the sticks. The HUD shows `IN UART COM8 50 Hz`.
5. Press the left stick's button to arm. Throttle up.

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
| "RC SIGNAL LOST" then a failsafe | Frames stopped for half a second — cable, USB port, or a reset |

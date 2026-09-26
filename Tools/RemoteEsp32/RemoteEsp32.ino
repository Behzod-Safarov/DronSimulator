// ---------------------------------------------------------------------------------------------
// DroneTrainer remote - ESP32-S3 + two analog thumbsticks.
//
// Streams RC channels down a UART in the format the simulator's SerialRc already decodes:
//
//   TEXT  "$RC,<roll>,<pitch>,<throttle>,<yaw>,<arm>,<mode>\n"   microseconds, 115200 baud
//   CRSF  0xC8 len 0x16 <16 x 11 bits> crc8                      the real ELRS/TBS frame, 420000
//
// Mode 2 layout, same as the sim: LEFT stick = throttle + yaw, RIGHT stick = pitch + roll.
// The left stick's push button is the arm switch, the right stick's cycles the flight mode.
//
// The remote sends sticks and switch positions and nothing else - rates, expo and the arming
// rules live in the flight controller, exactly as they do on a real radio.
// ---------------------------------------------------------------------------------------------

#include <Preferences.h>

// ------------------------------------------------------------------ what is wired where
// ADC1 pins only (GPIO1..GPIO10 on the S3). ADC2 is taken by the WiFi radio and reads garbage
// the moment WiFi is ever switched on, which is a horrible fault to debug later.
//
// The sticks on this remote are mounted turned 90 degrees (pin header on the side), so each
// module's VRx wire carries the stick's up/down and VRy carries left/right. Measured on the real
// remote: pushing up read as left, pushing right read as up. Hence up/down on the odd pins.
static const int PIN_THROTTLE = 1;   // left stick  up/down     (module VRx)
static const int PIN_YAW      = 2;   // left stick  left/right  (module VRy)
static const int PIN_PITCH    = 3;   // right stick up/down     (module VRx)
static const int PIN_ROLL     = 4;   // right stick left/right  (module VRy)
static const int PIN_ARM_BTN  = 5;   // left stick  SW  (to GND, internal pull-up)
static const int PIN_MODE_BTN = 6;   // right stick SW  (to GND, internal pull-up)

// Flip these if a stick moves the wrong way. Easier than re-soldering.
static const bool INVERT_ROLL     = true;
static const bool INVERT_PITCH    = true;
static const bool INVERT_YAW      = true;
static const bool INVERT_THROTTLE = true;

// ------------------------------------------------------------------ how it talks
#define USE_CRSF        0      // 0 = text frames (readable, easy to debug), 1 = real CRSF
#define USE_NATIVE_USB  1      // 1 = the S3's own USB port, 0 = UART1 out to a USB-TTL adapter
static const int PIN_UART_TX = 17;   // only used when USE_NATIVE_USB is 0
static const int PIN_UART_RX = 18;

static const int FRAME_HZ = 50;      // what a real radio sends; the sim fails safe after 0.5 s

// Wiring check: also print "# raw <roll> <pitch> <throttle> <yaw>  arm=<0|1> mode=<0|1>" twice a
// second - the ADC counts straight off the pins, before inversion or calibration. 0 means the pin
// sees 0 V (unwired, or the stick module has no power); a centred stick reads roughly 1700-2300.
// The game ignores any line that is not "$RC", so this is safe to leave on. Text format only.
#define DEBUG_RAW 0

#if USE_NATIVE_USB
  #define OUT Serial
#else
  #define OUT Serial1
#endif

// ------------------------------------------------------------------ feel
// Cheap thumbsticks spring back to centre, including the one being used as throttle - and a
// throttle that snaps to the middle when you let go is not a throttle. So by default the left
// stick's Y axis is treated as a throttle *rate*: push up to add power, pull down to take it
// away, let go and the setting stays. Set this to 0 for a real ratcheted throttle stick or a pot.
#define THROTTLE_SPRING_RETURN 1
static const float THROTTLE_RATE = 700.0f;   // microseconds per second at full deflection

static const int DEADBAND = 40;      // ADC counts around centre that count as "stick centred"
static const float SMOOTHING = 0.35f;  // 0..1 exponential filter; the ESP32 ADC is noisy

// ------------------------------------------------------------------ state
enum Axis { AX_ROLL, AX_PITCH, AX_THROTTLE, AX_YAW, AXIS_COUNT };

struct AxisCal { int16_t lo, mid, hi; };
struct Calibration {
  uint32_t magic;
  AxisCal axis[AXIS_COUNT];
};
// A calibration belongs to one pin map: move a pin or flip an INVERT flag and every stored number
// describes the wrong axis. The map is folded into the magic, so a changed map throws the old
// calibration away (the remote falls back to measuring centres at power-up) instead of flying on it.
static const uint32_t CAL_MAGIC = 0x52433032u
    ^ ((uint32_t)PIN_ROLL | (uint32_t)PIN_PITCH << 6 | (uint32_t)PIN_THROTTLE << 12 | (uint32_t)PIN_YAW << 18)
    ^ ((uint32_t)(INVERT_ROLL | INVERT_PITCH << 1 | INVERT_THROTTLE << 2 | INVERT_YAW << 3) << 24);

static Calibration cal;
static bool calibrated = false;
static float filtered[AXIS_COUNT];

static Preferences prefs;

static bool armed = false;
static int  modeIndex = 0;                       // 0 Angle, 1 Horizon, 2 Acro
static float throttleUs = 1000.0f;
static uint32_t armRefusedUntil = 0;

static const int PIN_OF[AXIS_COUNT] = { PIN_ROLL, PIN_PITCH, PIN_THROTTLE, PIN_YAW };
static const bool INVERT_OF[AXIS_COUNT] = { INVERT_ROLL, INVERT_PITCH, INVERT_THROTTLE, INVERT_YAW };

// ------------------------------------------------------------------ small helpers

static void led(uint8_t r, uint8_t g, uint8_t b) {
#ifdef RGB_BUILTIN
  #if defined(ESP_ARDUINO_VERSION_MAJOR) && ESP_ARDUINO_VERSION_MAJOR >= 3
    rgbLedWrite(RGB_BUILTIN, r, g, b);          // core 3.x name
  #else
    neopixelWrite(RGB_BUILTIN, r, g, b);        // core 2.x name
  #endif
#else
  (void)r; (void)g; (void)b;
#endif
}

static int readAxisRaw(int axis) {
  long sum = 0;
  for (int i = 0; i < 8; i++) sum += analogRead(PIN_OF[axis]);   // 8 samples kills most ADC hash
  int raw = (int)(sum / 8);
  if (INVERT_OF[axis]) raw = 4095 - raw;
  return raw;
}

// Raw ADC -> microseconds, using this stick's own travel. Split around the centre because a
// thumbstick is never symmetric: the two halves have different spans and a single scale factor
// leaves the stick sitting off-centre at one end.
static int axisToUs(int axis, int raw) {
  const AxisCal &c = cal.axis[axis];
  int centre = c.mid;
  if (raw > centre + DEADBAND) {
    int span = max(1, (int)c.hi - centre - DEADBAND);
    return 1500 + constrain((raw - centre - DEADBAND) * 500 / span, 0, 500);
  }
  if (raw < centre - DEADBAND) {
    int span = max(1, centre - DEADBAND - (int)c.lo);
    return 1500 - constrain((centre - DEADBAND - raw) * 500 / span, 0, 500);
  }
  return 1500;
}

// Full-travel version for a throttle that does not spring back: bottom of travel is 1000.
static int axisToThrottleUs(int axis, int raw) {
  const AxisCal &c = cal.axis[axis];
  int span = max(1, (int)c.hi - (int)c.lo);
  return 1000 + constrain((raw - c.lo) * 1000 / span, 0, 1000);
}

// The remote's own "throttle low to arm" rule. With a spring-return stick, idle means the stick is
// being left alone (centred); with a real throttle it means the bottom of travel. This also stops a
// throttle pin reading 0 V - unwired, or a stick module with no 3V3 - which the inverted axis
// reports as the stick held fully up: without this check the quad would arm and go straight to
// full power.
static bool throttleStickIdle() {
#if THROTTLE_SPRING_RETURN
  return axisToUs(AX_THROTTLE, (int)filtered[AX_THROTTLE]) == 1500;
#else
  return axisToThrottleUs(AX_THROTTLE, (int)filtered[AX_THROTTLE]) < 1050;
#endif
}

// A button that is pressed, released and debounced, reported as a single press event.
struct Button {
  int pin;
  bool last = true;
  uint32_t changedAt = 0;
  bool pressed() {
    bool now = digitalRead(pin);                 // pulled up: LOW means pressed
    uint32_t t = millis();
    if (now != last && t - changedAt > 25) {
      changedAt = t;
      last = now;
      return now == LOW;
    }
    return false;
  }
};
static Button armButton{PIN_ARM_BTN};
static Button modeButton{PIN_MODE_BTN};

// ------------------------------------------------------------------ calibration

static void loadCalibration() {
  prefs.begin("remote", true);
  size_t n = prefs.getBytesLength("cal");
  if (n == sizeof(Calibration)) {
    prefs.getBytes("cal", &cal, sizeof(Calibration));
    calibrated = (cal.magic == CAL_MAGIC);
  }
  prefs.end();

  if (!calibrated) {                             // usable defaults so it still flies untuned
    cal.magic = CAL_MAGIC;
    for (int i = 0; i < AXIS_COUNT; i++) cal.axis[i] = { 0, 2048, 4095 };
  }
}

static void saveCalibration() {
  cal.magic = CAL_MAGIC;
  prefs.begin("remote", false);
  prefs.putBytes("cal", &cal, sizeof(Calibration));
  prefs.end();
  calibrated = true;
}

// Held the arm button down at power-up: learn this pair of sticks.
// Six seconds of sweeping every stick into all four corners, then let go and sit still.
static void runCalibration() {
  led(60, 50, 0);
  for (int i = 0; i < AXIS_COUNT; i++) { cal.axis[i].lo = 4095; cal.axis[i].hi = 0; }

  uint32_t until = millis() + 6000;
  while (millis() < until) {
    for (int i = 0; i < AXIS_COUNT; i++) {
      int raw = readAxisRaw(i);
      if (raw < cal.axis[i].lo) cal.axis[i].lo = raw;
      if (raw > cal.axis[i].hi) cal.axis[i].hi = raw;
    }
    led(60, 50, 0);
    delay(20);
    if ((millis() / 250) % 2) led(0, 0, 0);      // blinking yellow: keep moving the sticks
  }

  led(0, 0, 60);                                 // blue: hands off, centres being measured
  delay(1200);
  for (int i = 0; i < AXIS_COUNT; i++) {
    long sum = 0;
    for (int s = 0; s < 32; s++) { sum += readAxisRaw(i); delay(4); }
    cal.axis[i].mid = (int16_t)(sum / 32);
    // A stick with no real travel would divide by nothing later; keep the ends honest.
    if (cal.axis[i].hi - cal.axis[i].lo < 200) { cal.axis[i].lo = 0; cal.axis[i].hi = 4095; }
  }
  saveCalibration();

  for (int i = 0; i < 3; i++) { led(0, 80, 0); delay(120); led(0, 0, 0); delay(120); }
}

// Never calibrated: take the sticks' resting position at power-up as their centre, so a fresh
// remote still flies straight and can pass the throttle-idle check before anyone calibrates it.
// A reading near either rail is a stick being held or a wiring fault, not a centre - that axis
// keeps the default.
static void captureCentres() {
  for (int i = 0; i < AXIS_COUNT; i++) {
    long sum = 0;
    for (int s = 0; s < 16; s++) { sum += readAxisRaw(i); delay(2); }
    int mid = (int)(sum / 16);
    if (mid > 600 && mid < 3500) cal.axis[i].mid = (int16_t)mid;
  }
}

// ------------------------------------------------------------------ frame builders

#if USE_CRSF
static uint8_t crc8(const uint8_t *p, int len) {
  uint8_t crc = 0;
  for (int i = 0; i < len; i++) {
    crc ^= p[i];
    for (int b = 0; b < 8; b++) crc = (crc & 0x80) ? (uint8_t)((crc << 1) ^ 0xD5) : (uint8_t)(crc << 1);
  }
  return crc;
}

static void sendFrame(const int *us) {
  uint8_t buf[26];
  buf[0] = 0xC8;
  buf[1] = 24;          // type + 22 payload bytes + crc
  buf[2] = 0x16;        // RC channels packed
  memset(buf + 3, 0, 22);

  int bit = 0;
  for (int ch = 0; ch < 16; ch++) {
    int v = constrain((int)lroundf((us[ch] - 1500) * 1.6f) + 992, 0, 0x7FF);  // 988..2012us -> 172..1811
    for (int b = 0; b < 11; b++, bit++)
      if (v & (1 << b)) buf[3 + (bit >> 3)] |= (uint8_t)(1 << (bit & 7));
  }
  buf[25] = crc8(buf + 2, 23);
  OUT.write(buf, 26);
}
#else
static void sendFrame(const int *us) {
  char line[96];
  int n = snprintf(line, sizeof(line), "$RC,%d,%d,%d,%d,%d,%d\n",
                   us[0], us[1], us[2], us[3], us[4], us[5]);
  OUT.write((const uint8_t *)line, n);
}
#endif

// ------------------------------------------------------------------ setup / loop

void setup() {
#if USE_CRSF
  const int baud = 420000;
#else
  const int baud = 115200;
#endif
#if USE_NATIVE_USB
  OUT.begin(baud);                               // USB CDC ignores the rate, the PC side sets it
#else
  OUT.begin(baud, SERIAL_8N1, PIN_UART_RX, PIN_UART_TX);
#endif

  pinMode(PIN_ARM_BTN, INPUT_PULLUP);
  pinMode(PIN_MODE_BTN, INPUT_PULLUP);
  analogReadResolution(12);
  analogSetAttenuation(ADC_11db);                // full 0..3.3 V swing from the sticks

  loadCalibration();

  delay(50);
  if (digitalRead(PIN_ARM_BTN) == LOW) runCalibration();
  else if (!calibrated) captureCentres();

  for (int i = 0; i < AXIS_COUNT; i++) filtered[i] = readAxisRaw(i);
}

void loop() {
  static uint32_t nextFrame = 0;
  static uint32_t lastFrame = 0;

  uint32_t now = millis();
  if ((int32_t)(now - nextFrame) < 0) return;
  nextFrame = now + 1000 / FRAME_HZ;
  float dt = lastFrame ? (now - lastFrame) / 1000.0f : 0.02f;
  lastFrame = now;

  for (int i = 0; i < AXIS_COUNT; i++)
    filtered[i] += (readAxisRaw(i) - filtered[i]) * SMOOTHING;

#if DEBUG_RAW && !USE_CRSF
  static uint32_t nextDebug = 0;
  if ((int32_t)(now - nextDebug) >= 0) {
    nextDebug = now + 500;
    char dbg[80];
    int n = snprintf(dbg, sizeof(dbg), "# raw %d %d %d %d  arm=%d mode=%d\n",
                     analogRead(PIN_ROLL), analogRead(PIN_PITCH),
                     analogRead(PIN_THROTTLE), analogRead(PIN_YAW),
                     digitalRead(PIN_ARM_BTN) == LOW, digitalRead(PIN_MODE_BTN) == LOW);
    OUT.write((const uint8_t *)dbg, n);
  }
#endif

  if (armButton.pressed()) {
    if (armed) armed = false;
    else if (throttleStickIdle()) {
      armed = true;
      // Every arm starts from closed throttle. The sim refuses to arm otherwise, and so does a
      // real flight controller - this way the switch always does what the pilot expects.
      throttleUs = 1000.0f;
    }
    else armRefusedUntil = now + 1500;
  }
  if (modeButton.pressed()) modeIndex = (modeIndex + 1) % 3;

#if THROTTLE_SPRING_RETURN
  int thrStick = axisToUs(AX_THROTTLE, (int)filtered[AX_THROTTLE]);   // 1000..2000, centred 1500
  throttleUs += ((thrStick - 1500) / 500.0f) * THROTTLE_RATE * dt;
  throttleUs = constrain(throttleUs, 1000.0f, 2000.0f);
  if (!armed) throttleUs = 1000.0f;
#else
  throttleUs = armed ? axisToThrottleUs(AX_THROTTLE, (int)filtered[AX_THROTTLE]) : 1000.0f;
#endif

  int us[16];
  for (int i = 0; i < 16; i++) us[i] = 1500;
  us[0] = axisToUs(AX_ROLL,  (int)filtered[AX_ROLL]);
  us[1] = axisToUs(AX_PITCH, (int)filtered[AX_PITCH]);
  us[2] = (int)throttleUs;
  us[3] = axisToUs(AX_YAW,   (int)filtered[AX_YAW]);
  us[4] = armed ? 2000 : 1000;
  us[5] = modeIndex == 0 ? 1000 : modeIndex == 1 ? 1500 : 2000;

  sendFrame(us);

  // Fast red blink: arming was just refused. Green armed, blue disarmed, slow red flicker if the
  // sticks were never calibrated.
  if ((int32_t)(armRefusedUntil - now) > 0) led((now / 100) % 2 ? 80 : 0, 0, 0);
  else if (!calibrated && (now / 400) % 2) led(60, 0, 0);
  else if (armed)                    led(0, 60, 0);
  else                               led(0, 0, 40);
}

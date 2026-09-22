using System.Text;
using UnityEngine;

/// <summary>
/// Immediate-mode on-screen display: telemetry, battery, lesson panel, crash
/// banner, stick overlay, key help, the joystick axis debug panel (F1) and the
/// settings panel (Tab) with the transmitter calibration wizard.
/// </summary>
public class Osd : MonoBehaviour
{
    public DronePhysics drone;
    public FlightController flightController;
    public CrashDetector crashDetector;
    public PilotInput input;
    public LessonManager lessons;
    public CameraSwitcher cameras;
    public MainMenu menu;
    public bool showAxisDebug;
    public bool showSticks = true;

    GUIStyle text, big, box, small, header;
    readonly StringBuilder sb = new StringBuilder();
    bool settingsOpen;
    bool pausedBySettings;
    public bool SettingsOpen => settingsOpen;
    StickCalibrator calibrator;
    Wind wind;
    FpvCameraEffects fpvFx;
    Camera fpvCamera;
    Texture2D dot;

    void Start()
    {
        calibrator = new StickCalibrator(input);
        wind = FindFirstObjectByType<Wind>();
        fpvFx = FindFirstObjectByType<FpvCameraEffects>();
        if (fpvFx != null) fpvCamera = fpvFx.GetComponent<Camera>();
        LoadSettings();
    }

    void Update()
    {
        if (input == null) return;
        if (input.DebugTogglePressed) showAxisDebug = !showAxisDebug;
        if (input.SettingsPressed) ToggleSettings();
        if (settingsOpen) calibrator.Update();
    }

    public void ToggleSettings()
    {
        settingsOpen = !settingsOpen;
        input.UiCapturesInput = settingsOpen;
        if (settingsOpen)
        {
            // Opening Settings mid-flight must actually pause the sim (like the Pause menu does),
            // not just zero the stick input - otherwise gravity/physics keep running while the
            // pilot is looking at sliders and the drone silently falls. Don't touch pause state
            // that a menu already owns (e.g. opened from the Pause screen).
            if (lessons != null && !lessons.Paused) { lessons.TogglePause(); pausedBySettings = true; }
        }
        else
        {
            calibrator.Cancel();
            SaveSettings();
            if (pausedBySettings) { if (lessons != null && lessons.Paused) lessons.TogglePause(); pausedBySettings = false; }
        }
    }

    void BuildStyles()
    {
        int fs = Mathf.Clamp(Screen.height / 50, 12, 26);
        text = new GUIStyle(GUI.skin.label) { fontSize = fs, richText = true };
        text.normal.textColor = Color.white;
        small = new GUIStyle(text) { fontSize = Mathf.RoundToInt(fs * 0.8f) };
        header = new GUIStyle(text) { fontStyle = FontStyle.Bold };
        big = new GUIStyle(text) { fontSize = fs * 2, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        box = new GUIStyle(GUI.skin.box);
        box.normal.background = Texture2D.grayTexture;
        dot = Texture2D.whiteTexture;
    }

    void Panel(Rect r)
    {
        GUI.color = new Color(0, 0, 0, 0.55f);
        GUI.Box(r, GUIContent.none, box);
        GUI.color = Color.white;
    }

    void OnGUI()
    {
        if (text == null) BuildStyles();
        float w = Screen.width, h = Screen.height;
        float pad = 12f;
        float line = text.fontSize * 1.25f;
        if (menu != null && menu.HidesHud) { if (settingsOpen) DrawSettings(w, h, line); return; }
        bool pausedByMenu = menu != null && menu.Current == MainMenu.State.Paused;

        // --- Telemetry (top-left) -------------------------------------------
        if (drone != null && flightController != null)
        {
            Vector3 v = drone.Body.GetVelocity();
            float hSpeed = new Vector2(v.x, v.z).magnitude;
            float rpm = 0f;
            for (int i = 0; i < 4; i++) rpm += drone.MotorOmega[i];
            rpm *= 9.5493f / 4f;

            sb.Clear();
            sb.AppendLine($"<b>{flightController.mode.ToString().ToUpper()}</b>   {(flightController.Armed ? "<color=#7CFC00>ARMED</color>" : "<color=#FF6060>DISARMED</color>")}");
            sb.AppendLine($"ALT  {drone.transform.position.y,6:0.0} m    VS {v.y,5:+0.0;-0.0} m/s");
            sb.AppendLine($"SPD  {hSpeed,6:0.0} m/s  ({hSpeed * 3.6f:0} km/h)");
            sb.AppendLine($"THR  {(input != null ? input.Throttle * 100f : 0f),5:0} %   RPM {rpm,6:0}");
            sb.AppendLine($"ROLL {flightController.RollDeg,6:0}°  PITCH {flightController.PitchDeg,5:0}°");
            string batColor = drone.BatteryDead ? "#FF4040" : drone.BatteryFraction < 0.2f ? "#FFB040" : "#FFFFFF";
            sb.AppendLine($"<color={batColor}>BAT  {drone.BatteryVolts,5:0.0} V  {drone.BatteryCurrent,5:0.0} A  {drone.BatteryFraction * 100f,3:0} %</color>   {drone.FlightSeconds / 60f:0}:{drone.FlightSeconds % 60f:00}");
            if (wind != null) sb.AppendLine($"WIND {drone.WindVelocity.magnitude,5:0.0} m/s");
            if (cameras != null) sb.AppendLine($"CAM  {cameras.CurrentName}    IN {(input != null ? input.Source : "-")}");
            Panel(new Rect(pad, pad, 420, line * 8.6f));
            GUI.Label(new Rect(pad + 8, pad + 4, 410, line * 9f), sb.ToString(), text);
        }

        // --- Lesson panel (top-right) -----------------------------------------
        if (lessons != null)
        {
            var l = lessons.Current;
            sb.Clear();
            float ph;
            if (l == null)
            {
                sb.AppendLine("<b>" + Loc.T("FREE FLIGHT") + "</b>   <size=" + small.fontSize + ">" + Loc.T("no scoring - just fly") + "</size>");
                sb.AppendLine("<size=" + small.fontSize + ">" + Loc.T("C camera   M mode   R fresh pack   F drop   Esc menu") + "</size>");
                ph = line * 2.6f;
            }
            else
            {
                var disc = lessons.CurrentDiscipline;
                int pos = disc != null ? disc.lessons.IndexOf(lessons.CurrentIndex) + 1 : 0;
                string best = new string('\u2605', l.BestStars).PadRight(3, '\u2606');
                sb.AppendLine($"<b>{Loc.T(l.discipline)} - {l.DisplayTitle}</b>   <size={small.fontSize}>{Loc.T("drill")} {pos}/{disc?.lessons.Count}   {Loc.T("best")} {best}   ({l.recommendedMode})</size>");
                sb.AppendLine(l.DisplayInstructions);
                sb.AppendLine();
                sb.AppendLine(l.Completed
                    ? $"<color=#7CFC00><b>{Loc.S(l.Status)}</b></color>   {Loc.T("Score")} {l.Score} / 100   {new string('\u2605', l.Stars).PadRight(3, '\u2606')}"
                    : $"<color=#FFD24D>{Loc.S(l.Status)}</color>");
                sb.AppendLine($"{Loc.T("Time")} {l.Elapsed:0.0} s   {Loc.T("Crashes")} {l.Crashes}");
                ph = line * 8.5f;
            }
            float pw = Mathf.Min(520, w * 0.42f);
            Panel(new Rect(w - pw - pad, pad, pw, ph));
            GUI.Label(new Rect(w - pw - pad + 8, pad + 4, pw - 16, ph), sb.ToString(), text);
        }

        // --- Centre banner ------------------------------------------------------
        string lessonBanner = lessons != null && lessons.Current != null ? Loc.S(lessons.Current.Banner) : null;
        if (pausedByMenu || settingsOpen) lessonBanner = null;
        if (!string.IsNullOrEmpty(lessonBanner))
        {
            GUI.color = new Color(1f, 0.95f, 0.5f);
            var huge = new GUIStyle(big) { fontSize = big.fontSize * 2 };
            GUI.Label(new Rect(0, h * 0.3f, w, huge.fontSize * 1.5f), lessonBanner, huge);
            GUI.color = Color.white;
        }
        if (!settingsOpen && lessons != null && Time.time - lessons.ToastTime < 3.5f && !string.IsNullOrEmpty(lessons.Toast))
        {
            GUI.color = new Color(1f, 0.85f, 0.3f);
            GUI.Label(new Rect(0, h * 0.2f, w, big.fontSize * 1.5f), Loc.S(lessons.Toast), big);
            GUI.color = Color.white;
        }
        if (pausedByMenu || settingsOpen) { }
        else if (drone != null && drone.Damaged && Mathf.Repeat(Time.time, 1f) < 0.6f)
        {
            GUI.color = new Color(1f, 0.5f, 0.2f);
            GUI.Label(new Rect(0, h * 0.62f, w, big.fontSize * 1.2f), Loc.S("PROP DAMAGED - land and press R"), big);
            GUI.color = Color.white;
        }
        else if (drone != null && drone.Propwash > 0.35f)
        {
            GUI.color = new Color(1f, 0.8f, 0.4f, 0.85f);
            GUI.Label(new Rect(0, h * 0.62f, w, big.fontSize * 1.2f), Loc.S("PROP WASH - add forward speed"), big);
            GUI.color = Color.white;
        }
        if (pausedByMenu || settingsOpen) { }
        else if (drone != null && drone.BatteryDead)
        {
            GUI.color = new Color(1f, 0.3f, 0.3f);
            GUI.Label(new Rect(0, h * 0.3f, w, big.fontSize * 1.5f), Loc.S("BATTERY EMPTY - press R for a fresh pack"), big);
            GUI.color = Color.white;
        }
        else if (crashDetector != null && Time.time - crashDetector.MessageTime < 2.5f && !string.IsNullOrEmpty(crashDetector.Message))
        {
            GUI.color = crashDetector.Crashed ? new Color(1f, 0.3f, 0.3f) : new Color(1f, 1f, 0.6f);
            GUI.Label(new Rect(0, h * 0.4f, w, big.fontSize * 1.5f), Loc.S(crashDetector.Message), big);
            GUI.color = Color.white;
        }
        else if (flightController != null && !flightController.Armed && (crashDetector == null || !crashDetector.Crashed) && !settingsOpen)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.8f);
            GUI.Label(new Rect(0, h * 0.4f, w, big.fontSize * 1.5f), Loc.S("Throttle down, press SPACE / (A) to arm"), big);
            GUI.color = Color.white;
        }
        else if (drone != null && drone.BatteryFraction < 0.15f && Mathf.Repeat(Time.time, 1f) < 0.5f)
        {
            GUI.color = new Color(1f, 0.7f, 0.2f);
            GUI.Label(new Rect(0, h * 0.12f, w, big.fontSize * 1.2f), Loc.S("LOW BATTERY - LAND"), big);
            GUI.color = Color.white;
        }

        if (lessons != null && lessons.Paused && !pausedByMenu && !settingsOpen)
            GUI.Label(new Rect(0, h * 0.5f, w, big.fontSize * 1.5f), Loc.T("PAUSED"), big);

        // --- Stick overlay (bottom centre) -----------------------------------------
        if (showSticks && input != null && !settingsOpen && !pausedByMenu)
        {
            float s = Mathf.Min(120f, h * 0.14f);
            float y = h - s - line * 2f;
            DrawStick(new Rect(w * 0.5f - s - 10f, y, s, s), input.Yaw, input.Throttle * 2f - 1f, "YAW / THR");
            DrawStick(new Rect(w * 0.5f + 10f, y, s, s), input.Roll, input.Pitch, "ROLL / PITCH");
        }

        // --- Key help (bottom) --------------------------------------------------
        const string help = "Arrows pitch/roll  W/S throttle  A/D yaw  |  Space arm  M mode  C camera  R reset  F drop  N next drill  Backspace retry  Esc menu  Tab settings  F1 axes";
        if (!pausedByMenu) GUI.Label(new Rect(pad, h - small.fontSize * 1.8f, w - 2 * pad, small.fontSize * 1.6f), help, small);

        // --- Joystick axis debug -------------------------------------------------
        if (showAxisDebug && input != null)
        {
            sb.Clear();
            sb.AppendLine($"<b>Input debug</b>  source: {input.Source}");
            sb.AppendLine($"raw  roll {input.Raw.x:+0.00;-0.00}  pitch {input.Raw.y:+0.00;-0.00}  thr {input.Raw.z:0.00}  yaw {input.Raw.w:+0.00;-0.00}");
            for (int i = 0; i < input.JoystickAxisValues.Count; i++)
                sb.AppendLine($"axis[{i}] = {input.JoystickAxisValues[i]:+0.00;-0.00}");
            if (input.JoystickAxisValues.Count == 0) sb.AppendLine("(no Joystick device - plug in an RC transmitter in USB joystick mode)");
            float dw = 420, dh = small.fontSize * (input.JoystickAxisValues.Count + 4) * 1.3f;
            Panel(new Rect(pad, h * 0.35f, dw, dh));
            GUI.Label(new Rect(pad + 8, h * 0.35f + 4, dw - 16, dh), sb.ToString(), small);
        }

        if (settingsOpen) DrawSettings(w, h, line);
    }

    void DrawStick(Rect r, float x, float y, string label)
    {
        GUI.color = new Color(0, 0, 0, 0.45f);
        GUI.DrawTexture(r, dot);
        GUI.color = new Color(1, 1, 1, 0.25f);
        GUI.DrawTexture(new Rect(r.center.x - 0.5f, r.y, 1, r.height), dot);
        GUI.DrawTexture(new Rect(r.x, r.center.y - 0.5f, r.width, 1), dot);
        GUI.color = new Color(0.4f, 0.9f, 1f, 0.95f);
        float px = r.x + (x * 0.5f + 0.5f) * r.width;
        float py = r.y + (1f - (y * 0.5f + 0.5f)) * r.height;
        GUI.DrawTexture(new Rect(px - 5, py - 5, 10, 10), dot);
        GUI.color = Color.white;
        GUI.Label(new Rect(r.x, r.yMax + 2, r.width, small.fontSize * 1.5f), label, small);
    }

    // ------------------------------------------------------------------ settings

    Vector2 scroll;

    void DrawSettings(float w, float h, float line)
    {
        float pw = Mathf.Min(620f, w - 40f), ph = Mathf.Min(h - 40f, line * 34f);
        var r = new Rect((w - pw) * 0.5f, (h - ph) * 0.5f, pw, ph);
        GUI.color = new Color(0.05f, 0.05f, 0.07f, 0.92f);
        GUI.Box(r, GUIContent.none, box);
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(r.x + 16, r.y + 10, r.width - 32, r.height - 20));
        GUILayout.Label("<b>SETTINGS</b>   (Tab to close - changes are saved)", header);
        scroll = GUILayout.BeginScrollView(scroll);

        GUILayout.Label("<b>Transmitter / input</b>", header);
        GUILayout.Label($"Detected: {input.Source}   joystick={(input.HasJoystick ? "yes" : "no")}  gamepad={(input.HasGamepad ? "yes" : "no")}", small);
        GUILayout.BeginHorizontal();
        if (!calibrator.Active && GUILayout.Button("Calibrate transmitter sticks", GUILayout.Height(line * 1.4f))) calibrator.Start();
        if (calibrator.Active && GUILayout.Button("Cancel", GUILayout.Height(line * 1.4f))) calibrator.Cancel();
        GUILayout.EndHorizontal();
        if (!string.IsNullOrEmpty(calibrator.Message))
            GUILayout.Label($"<color=#FFD24D>{calibrator.Message}</color>", text);
        for (int i = 0; i < 4; i++)
            GUILayout.Label($"  {(PilotInput.Channel)i,-9} axis {input.joyAxis[i]}{(input.joyInvert[i] ? " inverted" : "")}", small);
        input.deadband = Slider("Deadband", input.deadband, 0f, 0.15f, "0.00");
        input.springThrottle = GUILayout.Toggle(input.springThrottle, " Gamepad: throttle stick centre = 50%");

        GUILayout.Space(8);
        GUILayout.Label("<b>Rates (Betaflight Actual Rates)</b>", header);
        flightController.rollRates.centerSensitivity = flightController.pitchRates.centerSensitivity =
            Slider("Roll/Pitch centre sensitivity (deg/s)", flightController.rollRates.centerSensitivity, 20f, 200f, "0");
        flightController.rollRates.maxRate = flightController.pitchRates.maxRate =
            Slider("Roll/Pitch max rate (deg/s)", flightController.rollRates.maxRate, 200f, 1400f, "0");
        flightController.rollRates.expo = flightController.pitchRates.expo =
            Slider("Roll/Pitch expo", flightController.rollRates.expo, 0f, 1f, "0.00");
        flightController.yawRates.centerSensitivity = Slider("Yaw centre sensitivity", flightController.yawRates.centerSensitivity, 20f, 200f, "0");
        flightController.yawRates.maxRate = Slider("Yaw max rate", flightController.yawRates.maxRate, 200f, 1400f, "0");
        flightController.yawRates.expo = Slider("Yaw expo", flightController.yawRates.expo, 0f, 1f, "0.00");
        flightController.angleLimitDeg = Slider("Angle mode max tilt (deg)", flightController.angleLimitDeg, 20f, 80f, "0");

        GUILayout.Space(8);
        GUILayout.Label("<b>Flight</b>", header);
        flightController.beginnerThrottleAssist = GUILayout.Toggle(flightController.beginnerThrottleAssist, " Beginner throttle assist (stick centre = hover; NOT realistic)");
        flightController.airMode = GUILayout.Toggle(flightController.airMode, " Air mode");
        drone.infiniteBattery = GUILayout.Toggle(drone.infiniteBattery, " Infinite battery");
        drone.groundEffect = GUILayout.Toggle(drone.groundEffect, " Ground effect");
        if (wind != null)
        {
            wind.meanSpeed = Slider("Wind speed (m/s)", wind.meanSpeed, 0f, 12f, "0.0");
            wind.gustiness = Slider("Gustiness", wind.gustiness, 0f, 1f, "0.00");
            wind.turbulence = Slider("Turbulence (m/s)", wind.turbulence, 0f, 3f, "0.0");
        }

        GUILayout.Space(8);
        GUILayout.Label("<b>FPV camera</b>", header);
        if (fpvFx != null)
        {
            fpvFx.analogLook = GUILayout.Toggle(fpvFx.analogLook, " Analog video look (noise, breakup by range)");
            fpvFx.latencyMs = Slider("Video latency (ms)", fpvFx.latencyMs, 0f, 120f, "0");
            fpvFx.distortion = Slider("Lens distortion", fpvFx.distortion, 0f, 0.8f, "0.00");
            fpvFx.effectiveVerticalFov = Slider("Camera FOV (vertical, deg)", fpvFx.effectiveVerticalFov, 60f, 120f, "0");
        }
        if (cameras != null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("View:", GUILayout.Width(60));
            for (int i = 0; i < cameras.cameras.Length; i++)
                if (GUILayout.Toggle(cameras.current == i, " " + cameras.names[i]) && cameras.current != i) cameras.Select(i);
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(8);
        GUILayout.Label("<b>" + Loc.T("Language") + "</b>", header);
        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(Loc.Uz, " O'zbekcha") && !Loc.Uz) Loc.Lang = "uz";
        if (GUILayout.Toggle(!Loc.Uz, " English") && Loc.Uz) Loc.Lang = "en";
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
        GUILayout.Label("<b>Audio</b>", header);
        AudioListener.volume = Slider("Master volume", AudioListener.volume, 0f, 1f, "0.00");
        showSticks = GUILayout.Toggle(showSticks, " Show stick overlay");

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    float Slider(string label, float value, float min, float max, string fmt)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, small, GUILayout.Width(300));
        value = GUILayout.HorizontalSlider(value, min, max, GUILayout.MinWidth(120));
        GUILayout.Label(value.ToString(fmt), small, GUILayout.Width(60));
        GUILayout.EndHorizontal();
        return value;
    }

    void SaveSettings()
    {
        input.SaveMapping();
        PlayerPrefs.SetInt("input.spring", input.springThrottle ? 1 : 0);
        PlayerPrefs.SetFloat("rates.rp.center", flightController.rollRates.centerSensitivity);
        PlayerPrefs.SetFloat("rates.rp.max", flightController.rollRates.maxRate);
        PlayerPrefs.SetFloat("rates.rp.expo", flightController.rollRates.expo);
        PlayerPrefs.SetFloat("rates.yaw.center", flightController.yawRates.centerSensitivity);
        PlayerPrefs.SetFloat("rates.yaw.max", flightController.yawRates.maxRate);
        PlayerPrefs.SetFloat("rates.yaw.expo", flightController.yawRates.expo);
        PlayerPrefs.SetFloat("fc.angleLimit", flightController.angleLimitDeg);
        PlayerPrefs.SetInt("fc.assist", flightController.beginnerThrottleAssist ? 1 : 0);
        PlayerPrefs.SetInt("fc.airmode", flightController.airMode ? 1 : 0);
        PlayerPrefs.SetInt("drone.infiniteBattery", drone.infiniteBattery ? 1 : 0);
        PlayerPrefs.SetInt("drone.groundEffect", drone.groundEffect ? 1 : 0);
        if (wind != null)
        {
            PlayerPrefs.SetFloat("wind.mean", wind.meanSpeed);
            PlayerPrefs.SetFloat("wind.gust", wind.gustiness);
            PlayerPrefs.SetFloat("wind.turb", wind.turbulence);
        }
        if (fpvFx != null)
        {
            PlayerPrefs.SetInt("fpv.analog", fpvFx.analogLook ? 1 : 0);
            PlayerPrefs.SetFloat("fpv.latency", fpvFx.latencyMs);
            PlayerPrefs.SetFloat("fpv.distortion", fpvFx.distortion);
            PlayerPrefs.SetFloat("fpv.fov", fpvFx.effectiveVerticalFov);
        }
        PlayerPrefs.SetFloat("audio.volume", AudioListener.volume);
        PlayerPrefs.SetInt("osd.sticks", showSticks ? 1 : 0);
        PlayerPrefs.Save();
    }

    void LoadSettings()
    {
        if (!PlayerPrefs.HasKey("rates.rp.center")) return;
        input.springThrottle = PlayerPrefs.GetInt("input.spring", 1) == 1;
        flightController.rollRates.centerSensitivity = flightController.pitchRates.centerSensitivity = PlayerPrefs.GetFloat("rates.rp.center", 70f);
        flightController.rollRates.maxRate = flightController.pitchRates.maxRate = PlayerPrefs.GetFloat("rates.rp.max", 670f);
        flightController.rollRates.expo = flightController.pitchRates.expo = PlayerPrefs.GetFloat("rates.rp.expo", 0.54f);
        flightController.yawRates.centerSensitivity = PlayerPrefs.GetFloat("rates.yaw.center", 70f);
        flightController.yawRates.maxRate = PlayerPrefs.GetFloat("rates.yaw.max", 670f);
        flightController.yawRates.expo = PlayerPrefs.GetFloat("rates.yaw.expo", 0.54f);
        flightController.angleLimitDeg = PlayerPrefs.GetFloat("fc.angleLimit", 60f);
        flightController.beginnerThrottleAssist = PlayerPrefs.GetInt("fc.assist", 0) == 1;
        flightController.airMode = PlayerPrefs.GetInt("fc.airmode", 1) == 1;
        drone.infiniteBattery = PlayerPrefs.GetInt("drone.infiniteBattery", 0) == 1;
        drone.groundEffect = PlayerPrefs.GetInt("drone.groundEffect", 1) == 1;
        if (wind != null)
        {
            wind.meanSpeed = PlayerPrefs.GetFloat("wind.mean", wind.meanSpeed);
            wind.gustiness = PlayerPrefs.GetFloat("wind.gust", wind.gustiness);
            wind.turbulence = PlayerPrefs.GetFloat("wind.turb", wind.turbulence);
        }
        if (fpvFx != null)
        {
            fpvFx.analogLook = PlayerPrefs.GetInt("fpv.analog", 1) == 1;
            fpvFx.latencyMs = PlayerPrefs.GetFloat("fpv.latency", fpvFx.latencyMs);
            fpvFx.distortion = PlayerPrefs.GetFloat("fpv.distortion", fpvFx.distortion);
            fpvFx.effectiveVerticalFov = PlayerPrefs.GetFloat("fpv.fov", fpvFx.effectiveVerticalFov);
        }
        AudioListener.volume = PlayerPrefs.GetFloat("audio.volume", 1f);
        showSticks = PlayerPrefs.GetInt("osd.sticks", 1) == 1;
    }
}


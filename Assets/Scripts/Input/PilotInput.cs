using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Reads the pilot's sticks from (in priority order) a USB RC transmitter
/// (shows up as a generic Joystick), a gamepad, or the keyboard, and applies
/// a radio-style deadband. Layout is "Mode 2": left stick = throttle/yaw,
/// right stick = pitch/roll. Rates/expo live in <see cref="FlightController"/>.
///
/// Outputs: Throttle 0..1, Roll/Pitch/Yaw -1..1, plus one-frame button flags.
/// Runs before every other script so the flags are valid for the whole frame.
/// </summary>
[DefaultExecutionOrder(-100)]
public class PilotInput : MonoBehaviour
{
    public enum Channel { Roll = 0, Pitch = 1, Throttle = 2, Yaw = 3 }

    [Header("Sticks")]
    [Range(0f, 0.2f)] public float deadband = 0.03f;
    [Tooltip("Gamepad sticks spring back to centre; treat centre as 50% throttle")]
    public bool springThrottle = true;
    public float keyboardThrottleRate = 0.8f;

    [Header("Real transmitter over UART (serial) - takes priority when it has signal")]
    public SerialRc serialRc;

    [Header("USB RC transmitter (Joystick device) mapping - use the calibration wizard (Tab)")]
    public bool preferJoystick = true;
    public int[] joyAxis = { 0, 1, 2, 3 };       // per Channel
    public bool[] joyInvert = new bool[4];        // per Channel

    public float Throttle { get; private set; }
    public float Roll { get; private set; }
    public float Pitch { get; private set; }
    public float Yaw { get; private set; }
    public Vector4 Raw { get; private set; } // roll, pitch, throttle, yaw before deadband
    public string Source { get; private set; } = "none";
    public bool HasJoystick => Joystick.current != null;
    public bool HasGamepad => Gamepad.current != null;

    public bool ArmPressed { get; private set; }
    public bool ModePressed { get; private set; }
    public bool ResetPressed { get; private set; }
    public bool CameraPressed { get; private set; }
    public bool NextLessonPressed { get; private set; }
    public bool RestartLessonPressed { get; private set; }
    public bool PausePressed { get; private set; }
    public bool DebugTogglePressed { get; private set; }
    /// <summary>Toggle the picture-in-picture FPV monitor (V).</summary>
    public bool MonitorPressed { get; private set; }
    /// <summary>The UART radio.s arm switch went low - disarm now (failsafe included).</summary>
    public bool RcDisarmRequested { get; private set; }
    /// <summary>Flight mode selected by the radio.s 3-position switch.</summary>
    public FlightMode RcMode { get; private set; } = FlightMode.Angle;
    public bool RcModeChanged { get; private set; }
    public bool SettingsPressed { get; private set; }
    /// <summary>Release payload (F / right shoulder).</summary>
    public bool DropPressed { get; private set; }
    /// <summary>Scenario hot-key pressed this frame: 0 = free flight, 1..9 = scenario index, -1 = none.</summary>
    public int ScenarioKey { get; private set; } = -1;
    /// <summary>Physical stick values before any scripted override (used to detect the pilot taking over).</summary>
    public Vector4 PhysicalRaw { get; private set; }

    /// <summary>All analogue axes of the current joystick, in device order (for calibration/debug).</summary>
    public readonly List<float> JoystickAxisValues = new List<float>();

    // Scripted control (AutoPilotSelfTest / demos): replaces the physical sticks while active.
    public static bool OverrideActive;
    public static float OverrideRoll, OverridePitch, OverrideYaw, OverrideThrottle;
    public static bool OverrideArmPulse;
    public static bool OverrideDropPulse;

    /// <summary>When true the settings UI owns the keyboard; flight keys are ignored.</summary>
    public bool UiCapturesInput { get; set; }

    float keyboardThrottle;

    void Awake() => LoadMapping();

    /// <summary>Drop the keyboard's virtual throttle stick to zero (called on respawn).</summary>
    public void ResetKeyboardThrottle() => keyboardThrottle = 0f;

    void Update()
    {
        float roll = 0f, pitch = 0f, yaw = 0f, throttle = 0f;
        ArmPressed = ModePressed = ResetPressed = CameraPressed = false;
        NextLessonPressed = RestartLessonPressed = PausePressed = DebugTogglePressed = SettingsPressed = DropPressed = MonitorPressed = false;
        ScenarioKey = -1;
        RcDisarmRequested = RcModeChanged = false;

        var joystick = Joystick.current;
        var gamepad = Gamepad.current;
        var keyboard = Keyboard.current;

        // A real transmitter on the UART outranks everything else - that is the point of
        // plugging one in. It stays in charge until the link failsafes.
        if (serialRc != null && serialRc.HasSignal)
        {
            Source = "UART " + serialRc.Status;
            roll = serialRc.Roll;
            pitch = serialRc.Pitch;
            yaw = serialRc.Yaw;
            throttle = serialRc.Throttle;
            // Arming is a switch on a real radio, not a button: rising edge arms, falling edge disarms.
            ArmPressed |= serialRc.ArmSwitchPressed;
            RcDisarmRequested = serialRc.ArmSwitchReleased;
            RcMode = serialRc.ModeSwitch;
            RcModeChanged = serialRc.ModeSwitchChanged;
        }
        else if (joystick != null && (preferJoystick || gamepad == null))
        {
            Source = joystick.displayName;
            ReadJoystick(joystick, out roll, out pitch, out throttle, out yaw);
        }
        else if (gamepad != null)
        {
            Source = gamepad.displayName;
            Vector2 left = gamepad.leftStick.ReadValue();
            Vector2 right = gamepad.rightStick.ReadValue();
            roll = right.x;
            pitch = right.y;
            yaw = left.x;
            throttle = springThrottle ? (left.y + 1f) * 0.5f : Mathf.Clamp01(left.y);

            ArmPressed |= gamepad.buttonSouth.wasPressedThisFrame;
            ModePressed |= gamepad.buttonNorth.wasPressedThisFrame;
            ResetPressed |= gamepad.buttonEast.wasPressedThisFrame;
            CameraPressed |= gamepad.buttonWest.wasPressedThisFrame;
            NextLessonPressed |= gamepad.dpad.right.wasPressedThisFrame;
            RestartLessonPressed |= gamepad.dpad.left.wasPressedThisFrame;
            PausePressed |= gamepad.startButton.wasPressedThisFrame;
            SettingsPressed |= gamepad.selectButton.wasPressedThisFrame;
            DropPressed |= gamepad.rightShoulder.wasPressedThisFrame || gamepad.rightTrigger.wasPressedThisFrame;
        }
        else if (keyboard != null && !UiCapturesInput)
        {
            Source = "keyboard";
            roll = Axis(keyboard.rightArrowKey, keyboard.leftArrowKey);
            pitch = Axis(keyboard.upArrowKey, keyboard.downArrowKey);
            yaw = Axis(keyboard.dKey, keyboard.aKey);
            keyboardThrottle = Mathf.Clamp01(keyboardThrottle + Axis(keyboard.wKey, keyboard.sKey) * keyboardThrottleRate * Time.deltaTime);
            throttle = keyboardThrottle;
        }
        else if (keyboard != null)
        {
            Source = "keyboard";
            throttle = keyboardThrottle;
        }

        if (keyboard != null)
        {
            SettingsPressed |= keyboard.tabKey.wasPressedThisFrame;
            if (!UiCapturesInput)
            {
                ArmPressed |= keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame;
                ModePressed |= keyboard.mKey.wasPressedThisFrame;
                ResetPressed |= keyboard.rKey.wasPressedThisFrame;
                CameraPressed |= keyboard.cKey.wasPressedThisFrame;
                NextLessonPressed |= keyboard.nKey.wasPressedThisFrame;
                RestartLessonPressed |= keyboard.backspaceKey.wasPressedThisFrame;
                PausePressed |= keyboard.pKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame;
                DebugTogglePressed |= keyboard.f1Key.wasPressedThisFrame;
                MonitorPressed |= keyboard.vKey.wasPressedThisFrame;
                DropPressed |= keyboard.fKey.wasPressedThisFrame;
                if (keyboard.digit0Key.wasPressedThisFrame) ScenarioKey = 0;
                if (keyboard.digit1Key.wasPressedThisFrame) ScenarioKey = 1;
                if (keyboard.digit2Key.wasPressedThisFrame) ScenarioKey = 2;
                if (keyboard.digit3Key.wasPressedThisFrame) ScenarioKey = 3;
                if (keyboard.digit4Key.wasPressedThisFrame) ScenarioKey = 4;
                if (keyboard.digit5Key.wasPressedThisFrame) ScenarioKey = 5;
                if (keyboard.digit6Key.wasPressedThisFrame) ScenarioKey = 6;
                if (keyboard.digit7Key.wasPressedThisFrame) ScenarioKey = 7;
                if (keyboard.digit8Key.wasPressedThisFrame) ScenarioKey = 8;
                if (keyboard.digit9Key.wasPressedThisFrame) ScenarioKey = 9;
            }
        }

        if (UiCapturesInput)
        {
            // a menu owns the controls: no stick or button input reaches the flight controller
            roll = pitch = yaw = 0f;
            throttle = 0f;
            ArmPressed = ModePressed = ResetPressed = CameraPressed = NextLessonPressed = RestartLessonPressed = DropPressed = MonitorPressed = false;
            ScenarioKey = -1;
        }
        PhysicalRaw = new Vector4(roll, pitch, throttle, yaw);
        if (OverrideDropPulse) { DropPressed = true; OverrideDropPulse = false; }
        if (OverrideActive)
        {
            Source = "scripted";
            roll = OverrideRoll; pitch = OverridePitch; yaw = OverrideYaw; throttle = OverrideThrottle;
            if (OverrideArmPulse) { ArmPressed = true; OverrideArmPulse = false; }
        }

        Raw = new Vector4(roll, pitch, throttle, yaw);
        Roll = Deadband(roll);
        Pitch = Deadband(pitch);
        Yaw = Deadband(yaw);
        Throttle = Mathf.Clamp01(throttle);
    }

    void ReadJoystick(Joystick js, out float roll, out float pitch, out float throttle, out float yaw)
    {
        JoystickAxisValues.Clear();
        foreach (var c in js.allControls)
            if (c is AxisControl a && !(c is ButtonControl))
                JoystickAxisValues.Add(a.ReadValue());

        roll = ReadAxis(Channel.Roll);
        pitch = ReadAxis(Channel.Pitch);
        yaw = ReadAxis(Channel.Yaw);
        throttle = (ReadAxis(Channel.Throttle) + 1f) * 0.5f; // full stick travel -> 0..1

        // Generic buttons: first four map to arm / mode / reset / camera
        int b = 0;
        foreach (var c in js.allControls)
        {
            if (!(c is ButtonControl btn)) continue;
            if (btn.wasPressedThisFrame)
            {
                if (b == 0) ArmPressed = true;
                else if (b == 1) ModePressed = true;
                else if (b == 2) ResetPressed = true;
                else if (b == 3) CameraPressed = true;
            }
            b++;
        }
    }

    float ReadAxis(Channel ch)
    {
        int index = joyAxis[(int)ch];
        if (index < 0 || index >= JoystickAxisValues.Count) return 0f;
        float v = Mathf.Clamp(JoystickAxisValues[index], -1f, 1f);
        return joyInvert[(int)ch] ? -v : v;
    }

    static float Axis(ButtonControl positive, ButtonControl negative)
    {
        return (positive.isPressed ? 1f : 0f) - (negative.isPressed ? 1f : 0f);
    }

    float Deadband(float x)
    {
        x = Mathf.Clamp(x, -1f, 1f);
        float a = Mathf.Abs(x);
        if (a < deadband) return 0f;
        return Mathf.Sign(x) * (a - deadband) / (1f - deadband);
    }

    // ------------------------------------------------------------------ persistence

    public void SaveMapping()
    {
        for (int i = 0; i < 4; i++)
        {
            PlayerPrefs.SetInt($"joy.axis.{i}", joyAxis[i]);
            PlayerPrefs.SetInt($"joy.inv.{i}", joyInvert[i] ? 1 : 0);
        }
        PlayerPrefs.SetFloat("input.deadband", deadband);
        PlayerPrefs.Save();
    }

    public void LoadMapping()
    {
        if (!PlayerPrefs.HasKey("joy.axis.0")) return;
        for (int i = 0; i < 4; i++)
        {
            joyAxis[i] = PlayerPrefs.GetInt($"joy.axis.{i}", joyAxis[i]);
            joyInvert[i] = PlayerPrefs.GetInt($"joy.inv.{i}", 0) == 1;
        }
        deadband = PlayerPrefs.GetFloat("input.deadband", deadband);
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Calibration wizard for USB RC transmitters: for each channel it records the
/// resting axis values, asks the pilot to move one stick, and assigns the axis
/// that moved the most (with inversion so the positive direction is up/right/forward).
/// </summary>
public class StickCalibrator
{
    static readonly PilotInput.Channel[] Order =
    {
        PilotInput.Channel.Throttle, PilotInput.Channel.Yaw, PilotInput.Channel.Pitch, PilotInput.Channel.Roll,
    };

    static readonly string[] Prompts =
    {
        "Move the THROTTLE stick all the way UP, then release",
        "Move the YAW stick all the way RIGHT, then release",
        "Move the PITCH stick all the way FORWARD (up), then release",
        "Move the ROLL stick all the way RIGHT, then release",
    };

    readonly PilotInput input;
    readonly List<float> baseline = new List<float>();
    int step = -1;
    float settleUntil;
    bool waitingForRelease;

    public bool Active => step >= 0;
    public string Message { get; private set; } = "";
    public bool Finished { get; private set; }

    public StickCalibrator(PilotInput input) => this.input = input;

    public void Start()
    {
        if (input.JoystickAxisValues.Count == 0)
        {
            Message = "No USB joystick/transmitter detected. Plug it in (USB joystick / HID mode on the radio).";
            step = -1;
            Finished = false;
            return;
        }
        Finished = false;
        step = 0;
        BeginStep();
    }

    public void Cancel()
    {
        step = -1;
        Message = "Calibration cancelled";
    }

    void BeginStep()
    {
        baseline.Clear();
        baseline.AddRange(input.JoystickAxisValues);
        settleUntil = Time.unscaledTime + 0.4f;
        waitingForRelease = false;
        Message = $"Step {step + 1}/4: {Prompts[step]}";
    }

    public void Update()
    {
        if (!Active) return;
        var axes = input.JoystickAxisValues;
        if (axes.Count != baseline.Count) { Cancel(); return; }
        if (Time.unscaledTime < settleUntil) return;

        int best = -1;
        float bestDelta = 0f;
        for (int i = 0; i < axes.Count; i++)
        {
            float d = axes[i] - baseline[i];
            if (Mathf.Abs(d) > Mathf.Abs(bestDelta)) { bestDelta = d; best = i; }
        }

        if (!waitingForRelease)
        {
            if (best >= 0 && Mathf.Abs(bestDelta) > 0.6f)
            {
                var ch = Order[step];
                input.joyAxis[(int)ch] = best;
                input.joyInvert[(int)ch] = bestDelta < 0f;
                waitingForRelease = true;
                Message = $"{ch} = axis {best}{(bestDelta < 0f ? " (inverted)" : "")}  - release the stick";
            }
        }
        else if (Mathf.Abs(bestDelta) < 0.25f || Order[step] == PilotInput.Channel.Throttle && Time.unscaledTime > settleUntil + 2.5f)
        {
            step++;
            if (step >= Order.Length)
            {
                step = -1;
                Finished = true;
                input.SaveMapping();
                Message = "Calibration saved. Fly!";
            }
            else
            {
                BeginStep();
            }
        }
    }
}

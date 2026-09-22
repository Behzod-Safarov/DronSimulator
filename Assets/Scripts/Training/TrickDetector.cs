using System;
using UnityEngine;

/// <summary>
/// Recognises freestyle tricks from the drone's body rates: a full roll (360° about
/// the roll axis), a flip (360° about pitch) and a power loop (a flip that gains
/// height at the top). Rotation accumulates only while airborne and decays when the
/// quad is not rotating, so slow drift never adds up to a "trick".
/// </summary>
public class TrickDetector : MonoBehaviour
{
    public DronePhysics drone;
    public FlightController fc;
    public float completeDeg = 330f;
    public float powerLoopGain = 2.5f;
    [Tooltip("Tricks don't count below this altitude - a real safety-margin floor, not just OSD text")]
    public float minAltitude = 0f;

    public int Rolls { get; private set; }
    public int Flips { get; private set; }
    public int PowerLoops { get; private set; }
    public event Action<string> TrickDone;

    float rollAccum, pitchAccum;
    float flipStartAlt, flipMaxAlt;
    bool flipping;

    public void ResetCounts()
    {
        Rolls = Flips = PowerLoops = 0;
        rollAccum = pitchAccum = 0f;
        flipping = false;
    }

    void FixedUpdate()
    {
        if (drone == null || fc == null) return;
        float dt = Time.fixedDeltaTime;
        float alt = drone.transform.position.y;
        if (drone.IsGrounded || !fc.Armed || alt < minAltitude)
        {
            rollAccum = pitchAccum = 0f;
            flipping = false;
            return;
        }

        Vector3 w = transform.InverseTransformDirection(drone.Body.angularVelocity) * Mathf.Rad2Deg;

        // Roll about body Z
        if (Mathf.Abs(w.z) > 60f) rollAccum += w.z * dt; else rollAccum *= 1f - dt * 1.5f;
        if (Mathf.Abs(rollAccum) >= completeDeg && Mathf.Abs(fc.RollDeg) < 40f)
        {
            Rolls++;
            rollAccum = 0f;
            TrickDone?.Invoke("ROLL");
        }

        // Flip about body X (with altitude tracking for power loops)
        if (Mathf.Abs(w.x) > 60f)
        {
            // Track altitude from the moment the flip actually starts, not a quarter-turn in -
            // most of a power loop's height gain happens right at the start of the pull-up, so
            // starting late here was undercounting flipMaxAlt - flipStartAlt and misclassifying
            // real power loops as plain flips.
            if (!flipping) { flipping = true; flipStartAlt = alt; flipMaxAlt = alt; }
            pitchAccum += w.x * dt;
        }
        else pitchAccum *= 1f - dt * 1.5f;
        if (flipping) flipMaxAlt = Mathf.Max(flipMaxAlt, alt);
        if (Mathf.Abs(pitchAccum) >= completeDeg && Mathf.Abs(fc.PitchDeg) < 40f)
        {
            bool loop = flipping && flipMaxAlt - flipStartAlt > powerLoopGain;
            if (loop) PowerLoops++; else Flips++;
            pitchAccum = 0f;
            flipping = false;
            TrickDone?.Invoke(loop ? "POWER LOOP" : "FLIP");
        }
        if (Mathf.Abs(pitchAccum) < 20f) flipping = false;
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Game: kamikaze demolition. Fly the drone into the crate towers and barrel pyramids;
/// on impact it goes off like a firework and blasts the structure apart. You get a
/// fresh drone after each hit. Score = percentage of blocks knocked down with the
/// drones you have.
/// </summary>
public class DemolitionLesson : Lesson
{
    public DemoBlock[] blocks;
    public int drones = 5;
    public float blastRadius = 4.5f;
    public float blastForce = 1800f;
    [Tooltip("Impact speed needed to set the drone off (slower touches just bounce)")]
    public float triggerSpeed = 3f;

    [Header("Drill variants")]
    [Tooltip("If set, the drill is passed by hitting THIS block (e.g. the marked car); other hits waste a drone")]
    public DemoBlock requiredTarget;
    [Tooltip("Minimum impact speed for the hit to count (0 = any)")]
    public float minImpactSpeed = 0f;
    [Tooltip("Percentage of blocks that must be down to pass (only when no requiredTarget)")]
    [Range(1, 100)] public int passPercent = 100;

    public int DronesLeft { get; private set; }
    public int Toppled { get; private set; }
    public bool Exploded { get; private set; }
    public float LastImpactSpeed { get; private set; }
    public bool TargetHit { get; private set; }

    float respawnTimer;
    ImpactDetonator detonator;
    string banner;
    float bannerUntil;

    int Pct => Mathf.RoundToInt(100f * Toppled / Mathf.Max(1, blocks.Length));

    public override int Score
    {
        get
        {
            if (!Completed) return 0;
            int used = drones - DronesLeft;
            if (requiredTarget != null)
                return TargetHit ? Mathf.Clamp(100 - (used - 1) * 25, 10, 100) : 0;
            // Percentage drill: full marks for reaching the pass mark, bonus for spare drones -
            // proportional to how many drones this instance was given, not a flat +5 each (a
            // 3-drone precision variant and an 8-drone rubble-clearer would otherwise score on
            // wildly different bonus scales for the "same" spare-drones effort).
            float f = Mathf.Clamp01(Pct / (float)passPercent);
            float spareBonus = Pct >= passPercent ? (DronesLeft / (float)Mathf.Max(1, drones)) * 15f : 0f;
            return Mathf.Clamp(Mathf.RoundToInt(f * 85f + spareBonus), 0, 100);
        }
    }

    public override string Banner => Time.time < bannerUntil ? banner : null;

    public override string Status
    {
        get
        {
            int pct = Pct;
            string req = minImpactSpeed > 0f ? $"   (hit faster than {minImpactSpeed:0} m/s)" : "";
            if (Completed)
            {
                if (requiredTarget != null) return TargetHit ? $"TARGET HIT at {LastImpactSpeed:0.0} m/s with {drones - DronesLeft} drone(s)" : "Out of drones - target missed";
                return pct >= passPercent ? $"Passed: {pct}% demolished with {drones - DronesLeft} drones" : $"Out of drones - {pct}% demolished (needed {passPercent}%)";
            }
            if (Exploded) return $"BOOM!  {LastImpactSpeed:0.0} m/s   {pct}% demolished   next drone in {respawnTimer:0.0} s";
            if (requiredTarget != null) return $"Hit the MARKED target{req}   drones left {DronesLeft}";
            return $"Demolished {pct}% (need {passPercent}%){req}   drones left {DronesLeft}";
        }
    }

    public override void Begin(DronePhysics drone, FlightController fc, CrashDetector crash)
    {
        base.Begin(drone, fc, crash);
        DronesLeft = drones;
        Toppled = 0;
        Exploded = false;
        TargetHit = false;
        LastImpactSpeed = 0f;
        foreach (var b in blocks)
        {
            b.gameObject.SetActive(true);
            b.ResetBlock();
            b.GetComponent<CarWreckage>()?.Restore();
        }
        detonator = drone.GetComponent<ImpactDetonator>() ?? drone.gameObject.AddComponent<ImpactDetonator>();
        detonator.lesson = this;
        detonator.enabled = true;
        crash.suppressCrashes = true;
    }

    /// <summary>Called by the detonator when the drone hits a block hard enough.</summary>
    public void Detonate(Vector3 at, DemoBlock hitBlock = null, float impactSpeed = 0f)
    {
        if (Exploded || Completed) return;
        Exploded = true;
        DronesLeft--;
        respawnTimer = 2.2f;
        LastImpactSpeed = impactSpeed;
        bool fastEnough = impactSpeed >= minImpactSpeed;
        if (requiredTarget != null && hitBlock == requiredTarget && fastEnough) TargetHit = true;
        banner = requiredTarget != null && hitBlock == requiredTarget ? (fastEnough ? "TARGET HIT!" : "TOO SLOW") : "BOOM!";
        bannerUntil = Time.time + 1.5f;

        Fx.Burst(at, new Color(1f, 0.55f, 0.1f), 160, 14f, 0.12f, 1.3f);
        Fx.Burst(at, new Color(1f, 0.9f, 0.3f), 80, 9f, 0.08f, 0.9f, false);
        Fx.Burst(at + Vector3.up * 0.5f, new Color(0.32f, 0.3f, 0.27f, 0.85f), 70, 3.5f, 0.7f, 4.5f, false);
        Fx.PlayBoom(at);
        hitBlock?.GetComponent<CarWreckage>()?.Shatter();

        // Ground scorch mark - find the actual ground/asphalt surface under the blast, not the
        // (possibly elevated) impact point itself.
        Vector3 groundPos = at;
        if (Physics.Raycast(at + Vector3.up * 3f, Vector3.down, out var groundHit, 30f, ~0, QueryTriggerInteraction.Ignore))
            groundPos = groundHit.point;
        Fx.Splat(groundPos, 2.3f, new Color(0.03f, 0.03f, 0.03f, 0.6f), 60f);

        var blasted = new HashSet<Rigidbody>();
        foreach (var hit in Physics.OverlapSphere(at, blastRadius))
        {
            var rb = hit.attachedRigidbody;
            if (rb == null || rb == drone.Body || !blasted.Add(rb)) continue;
            bool isTarget = rb.GetComponent<DemoBlock>() != null;
            var joint = rb.GetComponent<Joint>(); // car wheels are attached with a breakable FixedJoint
            if (!isTarget && joint == null) continue;
            rb.WakeUp();
            if (joint != null) Object.Destroy(joint); // detach so it flies off on its own instead of yanking the body
            rb.AddExplosionForce(blastForce, at, blastRadius, 1.2f, ForceMode.Impulse);
        }

        // Hide the wreck until the next drone spawns (cameras/audio stay alive)
        fc.Disarm();
        SetDroneVisible(false);
    }

    public override void Tick(float dt)
    {
        base.Tick(dt);
        if (Completed) return;

        if (Exploded)
        {
            respawnTimer -= dt;
            if (respawnTimer <= 0f)
            {
                Exploded = false;
                SetDroneVisible(true);
                crash.Respawn();
            }
        }

        int count = 0;
        foreach (var b in blocks) if (b.IsToppled) count++;
        Toppled = count;

        bool passed = requiredTarget != null ? TargetHit : Toppled >= blocks.Length || Pct >= passPercent;
        if ((passed && !Exploded) || (DronesLeft <= 0 && !Exploded))
        {
            Complete();
            SetDroneVisible(true);
        }
    }

    void SetDroneVisible(bool on)
    {
        var model = drone.transform.Find("Model");
        if (model != null) model.gameObject.SetActive(on);
        drone.Body.isKinematic = !on;
        foreach (var col in drone.GetComponents<Collider>()) col.enabled = on;
    }

    public override void End()
    {
        if (detonator != null) detonator.enabled = false;
        if (crash != null) crash.suppressCrashes = false;
        if (drone != null) SetDroneVisible(true);
        foreach (var b in blocks) { b.ResetBlock(); b.gameObject.SetActive(false); }
    }
}

/// <summary>Watches the drone's collisions and sets it off when it hits a demolition block hard enough.</summary>
public class ImpactDetonator : MonoBehaviour
{
    public DemolitionLesson lesson;
    Rigidbody rb;

    void Awake() => rb = GetComponent<Rigidbody>();

    void OnCollisionEnter(Collision c)
    {
        if (!enabled || lesson == null) return;
        var block = c.collider.GetComponentInParent<DemoBlock>();
        if (block == null) return;
        if (c.relativeVelocity.magnitude < lesson.triggerSpeed) return;
        Vector3 at = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
        lesson.Detonate(at, block, c.relativeVelocity.magnitude);
    }
}

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

    public int DronesLeft { get; private set; }
    public int Toppled { get; private set; }
    public bool Exploded { get; private set; }

    float respawnTimer;
    ImpactDetonator detonator;

    public override int Score => Mathf.RoundToInt(100f * Toppled / Mathf.Max(1, blocks.Length));

    public override string Status
    {
        get
        {
            int pct = Mathf.RoundToInt(100f * Toppled / Mathf.Max(1, blocks.Length));
            if (Completed) return DronesLeft > 0 ? $"Everything down! {pct}% with {drones - DronesLeft} drones" : $"Out of drones - {pct}% demolished";
            return Exploded ? $"BOOM!  {pct}% demolished   next drone in {respawnTimer:0.0} s" : $"Demolished {pct}%   drones left {DronesLeft}";
        }
    }

    public override void Begin(DronePhysics drone, FlightController fc, CrashDetector crash)
    {
        base.Begin(drone, fc, crash);
        DronesLeft = drones;
        Toppled = 0;
        Exploded = false;
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
    public void Detonate(Vector3 at, DemoBlock hitBlock = null)
    {
        if (Exploded || Completed) return;
        Exploded = true;
        DronesLeft--;
        respawnTimer = 2.2f;

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

        if (Toppled >= blocks.Length || (DronesLeft <= 0 && !Exploded))
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
        lesson.Detonate(at, block);
    }
}

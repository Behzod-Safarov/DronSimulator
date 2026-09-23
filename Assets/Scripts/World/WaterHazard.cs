using UnityEngine;

/// <summary>
/// Water, as something that ends a flight.
///
/// The pond was a glossy disc with its collider deleted and the terrain basin left open
/// underneath, so a drone flown into it passed through the surface and settled on the mud at the
/// bottom - visibly submerged, still armed, no splash, no consequence. A real quad is finished the
/// instant it touches water: the pack shorts across the ESC and everything stops at once.
///
/// Sits on a trigger volume filling the water from the surface down to the basin floor, rather
/// than on the 4 cm-thick visual disc, so it cannot be tunnelled through at speed.
/// </summary>
public class WaterHazard : MonoBehaviour
{
    [Tooltip("World Y of the water surface, so the splash appears at the surface and not at depth")]
    public float surfaceY;
    public Color splashColor = new Color(0.62f, 0.8f, 0.86f);

    void OnTriggerEnter(Collider other)
    {
        var drone = other.GetComponentInParent<DronePhysics>();
        if (drone == null) return;

        var fc = drone.GetComponent<FlightController>();
        var crash = drone.GetComponent<CrashDetector>();
        if (crash != null && crash.Crashed) return;

        Vector3 at = drone.transform.position;
        at.y = surfaceY;
        // Two bursts: a fast white crown and a slower, wider spray.
        Fx.Burst(at, Color.white, 40, 7f, 0.05f, 0.8f);
        Fx.Burst(at, splashColor, 60, 3.5f, 0.09f, 1.4f);

        if (fc != null) fc.Disarm();
        if (crash != null) crash.ForceCrash("DITCHED - water kills a quad instantly");
    }
}

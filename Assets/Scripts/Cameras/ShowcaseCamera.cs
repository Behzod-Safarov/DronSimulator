using UnityEngine;

/// <summary>
/// Cinematic camera used behind the menus: slow orbits around points of interest
/// on the field, cutting to the next one every few seconds with a slight height
/// and radius drift so no two shots look the same. Purely presentational.
/// </summary>
public class ShowcaseCamera : MonoBehaviour
{
    [System.Serializable]
    public class Shot
    {
        public Transform target;
        public Vector3 offset;
        public float radius = 12f;
        public float height = 4f;
        public float degreesPerSecond = 6f;
        public float seconds = 11f;
        [Tooltip("How far above the target the camera looks (m)")]
        public float lookHeight = 0.6f;
    }

    public Shot[] shots;
    [Tooltip("Fraction of the shot radius / height the camera slowly drifts by")]
    public float radiusDrift = 0.2f;
    public float heightDrift = 0.25f;

    int index;
    float t;
    float angle;
    float seed;

    void OnEnable()
    {
        seed = Random.value * 100f;
        if (shots != null && shots.Length > 0) { index = Random.Range(0, shots.Length); angle = Random.value * 360f; }
        t = 0f;
    }

    void LateUpdate()
    {
        if (shots == null || shots.Length == 0) return;
        var s = shots[index];
        if (s.target == null) { Advance(); return; }

        float dt = Time.unscaledDeltaTime;
        t += dt;
        angle += s.degreesPerSecond * dt;
        if (t >= s.seconds) { Advance(); return; }

        float r = s.radius * (1f + Mathf.Sin(seed + t * 0.35f) * radiusDrift);
        float h = s.height * (1f + Mathf.Sin(seed * 1.7f + t * 0.27f) * heightDrift);
        Vector3 centre = s.target.position + s.offset;
        Vector3 pos = centre + new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad) * r, h, Mathf.Sin(angle * Mathf.Deg2Rad) * r);
        if (pos.y < 0.6f) pos.y = 0.6f;
        transform.position = pos;
        transform.rotation = Quaternion.LookRotation(centre + Vector3.up * s.lookHeight - pos, Vector3.up);
    }

    void Advance()
    {
        index = (index + 1) % shots.Length;
        t = 0f;
        angle = Random.value * 360f;
        seed = Random.value * 100f;
    }
}

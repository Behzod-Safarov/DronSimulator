using UnityEngine;

/// <summary>Attached to a target car's body: hides the glass and sprays shard particles when the car is destroyed.</summary>
public class CarWreckage : MonoBehaviour
{
    public Renderer[] glassRenderers = new Renderer[0];
    bool shattered;

    public void Shatter()
    {
        if (shattered) return;
        shattered = true;
        foreach (var r in glassRenderers)
        {
            if (r == null) continue;
            Fx.Burst(r.bounds.center, new Color(0.75f, 0.85f, 0.9f, 0.9f), 16, 4f, 0.03f, 1.1f);
            r.enabled = false;
        }
    }

    public void Restore()
    {
        shattered = false;
        foreach (var r in glassRenderers)
            if (r != null) r.enabled = true;
    }
}

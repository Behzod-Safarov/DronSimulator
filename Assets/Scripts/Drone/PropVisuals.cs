using UnityEngine;

/// <summary>
/// Swaps the modelled tri-blade props for a translucent "motion blur" disc once
/// the motors spin fast, like a prop looks on camera.
/// </summary>
public class PropVisuals : MonoBehaviour
{
    public DronePhysics drone;
    public GameObject[] blades = new GameObject[4];
    public Renderer[] discs = new Renderer[4];
    [Range(0f, 1f)] public float blurStartFraction = 0.12f;
    [Range(0f, 1f)] public float discMaxAlpha = 0.28f;

    MaterialPropertyBlock block;
    Color discColor;

    void Start()
    {
        block = new MaterialPropertyBlock();
        if (discs[0] != null) discColor = discs[0].sharedMaterial.color;
    }

    void Update()
    {
        if (drone == null) return;
        block ??= new MaterialPropertyBlock(); // survives a script reload in play mode
        float omegaMax = Mathf.Max(1f, drone.OmegaMaxAtFullBattery);
        for (int i = 0; i < 4; i++)
        {
            // A struck prop (DronePhysics.MotorHealth < 1) still physically spins and produces
            // partial thrust - it never fully vanishes, it only ever loses 25-45% health - so the
            // blades/disc should just look weaker, not disappear (that used to contradict both the
            // physics and the "the quad keeps flying, badly" message).
            float health = drone.MotorHealth[i];
            float f = drone.MotorOmega[i] / omegaMax;
            bool blur = f > blurStartFraction;
            if (blades[i] != null && blades[i].activeSelf == blur) blades[i].SetActive(!blur);
            if (discs[i] != null)
            {
                float a = (blur ? Mathf.Lerp(0.1f, discMaxAlpha, Mathf.Clamp01((f - blurStartFraction) / 0.3f)) : 0f) * health;
                discs[i].enabled = a > 0.001f;
                block.Clear();
                block.SetColor("_Color", new Color(discColor.r, discColor.g, discColor.b, a));
                discs[i].SetPropertyBlock(block);
            }
        }
    }
}

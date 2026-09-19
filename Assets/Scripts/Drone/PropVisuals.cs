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
            float f = drone.MotorOmega[i] / omegaMax;
            bool blur = f > blurStartFraction;
            if (blades[i] != null && blades[i].activeSelf == blur) blades[i].SetActive(!blur);
            if (discs[i] != null)
            {
                float a = blur ? Mathf.Lerp(0.1f, discMaxAlpha, Mathf.Clamp01((f - blurStartFraction) / 0.3f)) : 0f;
                discs[i].enabled = a > 0.001f;
                discs[i].GetPropertyBlock(block);
                block.SetColor("_Color", new Color(discColor.r, discColor.g, discColor.b, a));
                discs[i].SetPropertyBlock(block);
            }
        }
    }
}

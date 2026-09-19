using System;
using UnityEngine;

/// <summary>A dropped water balloon: falls with drag, splashes on the first thing it touches.</summary>
[RequireComponent(typeof(Rigidbody))]
public class WaterBalloon : MonoBehaviour
{
    public float dragCoefficient = 0.02f; // F = -k |v| v
    public event Action<WaterBalloon, Vector3> Splashed;

    Rigidbody rb;
    bool done;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.SetDamping(0f, 0.05f);
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    void FixedUpdate()
    {
        Vector3 v = rb.GetVelocity();
        rb.AddForce(-dragCoefficient * v.magnitude * v);
        if (transform.position.y < -5f) Destroy(gameObject);
    }

    void OnCollisionEnter(Collision c)
    {
        if (done) return;
        done = true;
        Vector3 p = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
        Fx.Burst(p, new Color(0.4f, 0.7f, 1f, 0.9f), 80, 5f, 0.05f, 0.9f);
        Fx.Splat(p, 0.6f, new Color(0.15f, 0.2f, 0.35f, 0.55f));
        Fx.PlaySplash(p);
        Splashed?.Invoke(this, p);
        Destroy(gameObject);
    }

    public static WaterBalloon Spawn(Vector3 position, Vector3 velocity, float mass)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Water balloon";
        go.transform.position = position;
        go.transform.localScale = Vector3.one * 0.12f;
        var mr = go.GetComponent<Renderer>();
        var m = new Material(Shader.Find("Standard"));
        m.SetFloat("_Mode", 2f);
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_ALPHABLEND_ON");
        m.renderQueue = 3000;
        m.color = new Color(0.35f, 0.6f, 1f, 0.75f);
        m.SetFloat("_Glossiness", 0.9f);
        mr.material = m;
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = mass;
        rb.SetVelocity(velocity);
        return go.AddComponent<WaterBalloon>();
    }
}

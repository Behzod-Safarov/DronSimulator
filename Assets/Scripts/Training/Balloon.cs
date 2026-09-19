using System;
using UnityEngine;

/// <summary>A helium balloon on a string. Flying the drone into it pops it.</summary>
[RequireComponent(typeof(SphereCollider))]
public class Balloon : MonoBehaviour
{
    public Renderer body;
    public Color color = Color.red;
    public float bobAmplitude = 0.25f;
    public float bobSpeed = 0.7f;

    public event Action<Balloon> Popped;
    public bool IsPopped { get; private set; }

    Vector3 basePos;
    bool hasBase;
    float phase;

    void Awake()
    {
        GetComponent<SphereCollider>().isTrigger = true;
        CaptureBase();
        phase = UnityEngine.Random.Range(0f, 10f);
    }

    void CaptureBase()
    {
        if (hasBase) return;
        basePos = transform.position;
        hasBase = true;
    }

    void Update()
    {
        if (IsPopped) return;
        float t = Time.time * bobSpeed + phase;
        transform.position = basePos + new Vector3(Mathf.Sin(t * 0.7f) * 0.15f, Mathf.Sin(t) * bobAmplitude, Mathf.Cos(t * 0.5f) * 0.15f);
    }

    void OnTriggerEnter(Collider other)
    {
        if (IsPopped) return;
        if (other.attachedRigidbody == null || other.attachedRigidbody.GetComponent<DronePhysics>() == null) return;
        Pop();
    }

    public void Pop()
    {
        if (IsPopped) return;
        IsPopped = true;
        Fx.Burst(transform.position, color, 40, 4f, 0.05f, 0.8f);
        Fx.PlayPop(transform.position);
        Popped?.Invoke(this);
        gameObject.SetActive(false);
    }

    public void Restore()
    {
        CaptureBase();
        IsPopped = false;
        transform.position = basePos;
        gameObject.SetActive(true);
    }
}

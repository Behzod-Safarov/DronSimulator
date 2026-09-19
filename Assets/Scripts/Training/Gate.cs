using System;
using UnityEngine;

/// <summary>A race gate. Fires Passed when the drone flies through its trigger volume.</summary>
[RequireComponent(typeof(BoxCollider))]
public class Gate : MonoBehaviour
{
    public enum State { Idle, Next, Passed }

    public Renderer[] markers;
    public Color idleColor = new Color(0.6f, 0.6f, 0.65f);
    public Color nextColor = new Color(1f, 0.55f, 0.1f);
    public Color passedColor = new Color(0.25f, 0.9f, 0.35f);

    public event Action<Gate> Passed;

    MaterialPropertyBlock block;

    void Awake()
    {
        GetComponent<BoxCollider>().isTrigger = true;
        SetState(State.Idle);
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.attachedRigidbody != null && other.attachedRigidbody.GetComponent<DronePhysics>() != null)
            Passed?.Invoke(this);
    }

    public void SetState(State state)
    {
        if (markers == null) return;
        block ??= new MaterialPropertyBlock();
        Color c = state switch
        {
            State.Next => nextColor,
            State.Passed => passedColor,
            _ => idleColor,
        };
        foreach (var r in markers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor("_Color", c);
            block.SetColor("_EmissionColor", state == State.Next ? c * 0.8f : Color.black);
            r.SetPropertyBlock(block);
        }
    }
}

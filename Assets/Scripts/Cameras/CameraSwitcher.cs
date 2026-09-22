using UnityEngine;

/// <summary>Cycles between the available cameras (Chase -> Line of sight -> FPV) and moves the audio listener with them.</summary>
public class CameraSwitcher : MonoBehaviour
{
    public Camera[] cameras;
    public string[] names;
    public int current;
    public PilotInput input;
    /// <summary>While true (menus up) every gameplay camera and its listener stay off.</summary>
    public bool suppressed;

    public string CurrentName => names != null && current < names.Length ? names[current] : $"Camera {current}";
    public Camera Current => cameras != null && current < cameras.Length ? cameras[current] : null;

    void Start()
    {
        current = PlayerPrefs.GetInt("camera.index", current);
        Apply();
    }

    void Update()
    {
        if (input != null && input.CameraPressed) Next();
    }

    public void Next() => Select((current + 1) % Mathf.Max(1, cameras.Length));

    public void Select(int index)
    {
        if (cameras == null || cameras.Length == 0) return;
        current = Mathf.Clamp(index, 0, cameras.Length - 1);
        PlayerPrefs.SetInt("camera.index", current);
        Apply();
    }

    public void Apply()
    {
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] == null) continue;
            bool on = !suppressed && i == current;
            cameras[i].enabled = on;
            var listener = cameras[i].GetComponent<AudioListener>();
            if (listener == null) listener = cameras[i].gameObject.AddComponent<AudioListener>();
            listener.enabled = on;
        }
    }
}

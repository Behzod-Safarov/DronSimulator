using System;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Development remote control for automated testing: start with
/// <c>-droneRemote C:\path\cmd.txt</c> and the sim polls that file, executes
/// each line as a command and deletes it. Commands:
///   camera N | arm | disarm | reset | mode angle|horizon|acro | lesson N
///   sticks roll pitch yaw throttle   (scripted sticks, -1..1 / 0..1)
///   release                          (back to physical sticks)
///   wind speed | shot path.png | settings | log | refresh (hot-recompile)
///   drop | goto x y z (autopilot) | apradius r | balloons | targets | terrain | chase dist height
///   menu [title] | nav <left|right|up|down|confirm|back|esc|f|l|0-9> | blocks (demolition state)
/// </summary>
public class DevRemote : MonoBehaviour
{
    public DronePhysics drone;
    public FlightController flightController;
    public CrashDetector crashDetector;
    public CameraSwitcher cameras;
    public LessonManager lessons;
    public Osd osd;
    public MainMenu menu;

    string path;
    float next;

    void Start()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-droneRemote");
        if (i >= 0 && i + 1 < args.Length)
        {
            path = args[i + 1];
            Debug.Log($"[DevRemote] listening on {path}");
        }
        else enabled = false;
    }

    float nextHeartbeat;

    void Update()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.1f;
        bool exists = File.Exists(path);
        if (Time.unscaledTime > nextHeartbeat)
        {
            nextHeartbeat = Time.unscaledTime + 15f;
            Debug.Log($"[DevRemote] polling {path} exists={exists}");
        }
        if (!exists) return;

        string[] lines;
        try { lines = File.ReadAllLines(path); File.Delete(path); }
        catch (IOException) { return; }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            try { Exec(line); }
            catch (Exception e) { Debug.LogWarning($"[DevRemote] '{line}' failed: {e.Message}"); }
        }
    }

    string ApState()
    {
        var ap = drone.GetComponent<Autopilot>();
        return ap == null ? "-" : $"{ap.State}/{ap.Message}/ovr={PilotInput.OverrideActive}/thr={PilotInput.OverrideThrottle:0.00}";
    }

    static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    void Exec(string line)
    {
        var p = line.Split(' ');
        switch (p[0].ToLowerInvariant())
        {
            case "camera": cameras.Select(int.Parse(p[1])); break;
            case "chase":
            {
                var chase = FindFirstObjectByType<ChaseCamera>();
                chase.distance = F(p[1]);
                chase.height = F(p[2]);
                break;
            }
            case "arm": flightController.Arm(); break;
            case "disarm": flightController.Disarm(); break;
            case "reset": crashDetector.Respawn(); break;
            case "mode": flightController.mode = (FlightMode)Enum.Parse(typeof(FlightMode), p[1], true); break;
            case "lesson": if (menu != null) menu.PlayLesson(int.Parse(p[1])); else lessons.StartLesson(int.Parse(p[1])); break;
            case "menu": if (menu != null) { if (p.Length > 1 && p[1] == "title") menu.OpenTitle(); else menu.OpenDrills(); } break;
            case "nav": if (menu != null) menu.Nav(p[1]); break;
            case "discipline": lessons.StartDiscipline(int.Parse(p[1])); break;
            case "nextdrill": lessons.NextDrill(); break;
            case "drills":
            {
                var sb = new System.Text.StringBuilder("[DevRemote] drills:");
                for (int i = 0; i < lessons.Lessons.Length; i++) sb.Append($" [{i}] {lessons.Lessons[i].discipline}/{lessons.Lessons[i].title} best={lessons.Lessons[i].BestStars}");
                Debug.Log(sb.ToString());
                break;
            }
            case "sticks":
                PilotInput.OverrideRoll = F(p[1]);
                PilotInput.OverridePitch = F(p[2]);
                PilotInput.OverrideYaw = F(p[3]);
                PilotInput.OverrideThrottle = F(p[4]);
                PilotInput.OverrideActive = true;
                break;
            case "release": PilotInput.OverrideActive = false; break;
            case "drop": PilotInput.OverrideDropPulse = true; break;
            case "goto":
            {
                var ap = drone.GetComponent<Autopilot>();
                var pt = new Vector3(F(p[1]), F(p[2]), F(p[3]));
                ap.StartMission(new[] { pt }, pt.y);
                break;
            }
            case "bounds":
            {
                var col = drone.GetComponent<Collider>();
                var rs = drone.GetComponentsInChildren<Renderer>();
                var vis = new Bounds(drone.transform.position, Vector3.zero);
                foreach (var rr in rs) if (rr.enabled && !(rr is ParticleSystemRenderer)) vis.Encapsulate(rr.bounds);
                string hitText = "none";
                if (Physics.Raycast(drone.transform.position + Vector3.up * 2f, Vector3.down, out var hit, 10f, ~0, QueryTriggerInteraction.Ignore))
                    hitText = $"{hit.collider.name} y={hit.point.y:0.0000}";
                Debug.Log($"[DevRemote] root y={drone.transform.position.y:0.0000}  collider min y={col.bounds.min.y:0.0000}  visual min y={vis.min.y:0.0000}  surface under: {hitText}");
                break;
            }
            case "blocks":
            {
                var dl = lessons.Current as DemolitionLesson;
                if (dl == null) { Debug.Log("[DevRemote] current drill is not a demolition drill"); break; }
                int shown = 0;
                foreach (var b in dl.blocks)
                {
                    if (!b.IsToppled || shown++ > 12) continue;
                    Debug.Log($"[DevRemote] toppled {b.name} at {b.transform.position} drift={b.Drift:0.00} m tilt={b.Tilt:0} deg");
                }
                Debug.Log($"[DevRemote] blocks={dl.blocks.Length} toppled={dl.Toppled}");
                break;
            }
            case "targets":
            {
                var l = FindFirstObjectByType<WaterBalloonDropLesson>();
                foreach (var t in l.targets) Debug.Log($"[DevRemote] target {t.name} at {t.position} active={t.gameObject.activeInHierarchy}");
                break;
            }
            case "terrain":
            {
                var tc = FindFirstObjectByType<TerrainCollider>();
                var t = FindFirstObjectByType<Terrain>();
                Debug.Log($"[DevRemote] terrainCollider={(tc != null)} enabled={tc?.enabled} data={(tc?.terrainData != null ? tc.terrainData.name : "null")} bounds={tc?.bounds} sameData={(tc != null && t != null && tc.terrainData == t.terrainData)} hmRes={t?.terrainData.heightmapResolution} sample={t?.SampleHeight(drone.transform.position)}");
                bool hit = Physics.Raycast(drone.transform.position + Vector3.up * 50f, Vector3.down, out var h, 200f, ~0, QueryTriggerInteraction.Ignore);
                Debug.Log($"[DevRemote] raycast from +50: hit={hit} {(hit ? h.collider.name + " at " + h.point : "")}");
                break;
            }
            case "apradius": drone.GetComponent<Autopilot>().waypointRadius = F(p[1]); break;
            case "apstop": drone.GetComponent<Autopilot>().Stop(); PilotInput.OverrideActive = false; break;
            case "balloons":
            {
                var sb = new System.Text.StringBuilder("[DevRemote] balloons:");
                foreach (var b in FindObjectsByType<Balloon>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    sb.Append($" {b.name}@({b.transform.position.x:0.0},{b.transform.position.y:0.0},{b.transform.position.z:0.0}){(b.IsPopped ? "x" : "")}");
                Debug.Log(sb.ToString());
                break;
            }
            case "refresh":
#if UNITY_EDITOR
                UnityEditor.AssetDatabase.Refresh(); // recompile changed scripts without needing editor focus
#endif
                break;
            case "wind": if (Wind.Instance != null) Wind.Instance.meanSpeed = F(p[1]); break;
            case "assist": flightController.beginnerThrottleAssist = p[1] == "on"; break;
            case "shot": ScreenCapture.CaptureScreenshot(line.Substring(5).Trim(), 1); break;
            case "settings": if (osd != null) osd.ToggleSettings(); break;
            case "log":
            {
                Vector3 pos = drone.transform.position;
                Debug.Log($"[DevRemote] alt={pos.y:0.00} pos=({pos.x:0.0},{pos.z:0.0}) v={drone.Body.GetVelocity().magnitude:0.00} armed={flightController.Armed} mode={flightController.mode} cam={cameras.CurrentName} bat={drone.BatteryVolts:0.0}V crashed={crashDetector.Crashed} msg=[{crashDetector.Message}] ts={Time.timeScale:0.0} ap={ApState()} menu={(menu != null ? menu.Current.ToString() : "-")} agl={drone.HeightAboveGround:0.00} lesson={(lessons.Current != null ? lessons.Current.title + " | " + lessons.Current.Status : "free")}");
                break;
            }
            default: Debug.LogWarning($"[DevRemote] unknown command '{line}'"); break;
        }
        Debug.Log($"[DevRemote] ok: {line}");
    }
}

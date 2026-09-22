using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Front end of the trainer: title screen, discipline and drill selection, launch
/// transition, in-flight pause menu and the results card after every drill.
/// Drawn with IMGUI over the <see cref="ShowcaseCamera"/>; works with mouse,
/// keyboard (arrows / WASD / digits / Enter / Esc) and gamepad (d-pad, A, B).
/// </summary>
public class MainMenu : MonoBehaviour
{
    public LessonManager lessons;
    public CameraSwitcher cameras;
    public Camera showcaseCamera;
    public PilotInput input;
    public FlightController flightController;
    public Osd osd;
    [Tooltip("Skip straight into free flight (used by the automated self-test)")]
    public bool skipMenu;

    public enum State { Title, Disciplines, Drills, Launching, Intro, Playing, Paused, Results }
    public State Current { get; private set; } = State.Title;

    /// <summary>True while a full-screen menu is up and the flight HUD should not be drawn.</summary>
    public bool HidesHud => Current == State.Title || Current == State.Disciplines || Current == State.Drills || Current == State.Launching || Current == State.Results;
    /// <summary>True while a menu owns the keyboard/gamepad.</summary>
    public bool CapturesInput => Current != State.Playing && Current != State.Intro;

    // ---- discipline presentation ------------------------------------------------
    class Look { public Color accent; public string tagline, blurb; public int icon; }
    static readonly Dictionary<string, Look> Looks = new Dictionary<string, Look>
    {
        ["Basics"] = new Look { accent = new Color(0.35f, 0.78f, 1f), tagline = "Hover  ·  Land  ·  Orbit", icon = 0,
            blurb = "What a 5-inch quad really feels like: throttle finesse, killing drift, soft landings and the coordinated turn. Start here." },
        ["Racing"] = new Look { accent = new Color(1f, 0.6f, 0.18f), tagline = "Gates  ·  Laps  ·  Time trial", icon = 1,
            blurb = "The gate loop from a gentle Angle-mode lap to a full Acro race with a 3-2-1 countdown, false-start detection and a lap record to beat." },
        ["Freestyle"] = new Look { accent = new Color(0.78f, 0.45f, 1f), tagline = "Rolls  ·  Flips  ·  Power loops", icon = 2,
            blurb = "Acro tricks recognised from the quad's real body rates. Build the muscle memory to throw and catch a roll before you try it outside." },
        ["Demolition"] = new Look { accent = new Color(1f, 0.32f, 0.32f), tagline = "Balloons  ·  Drops  ·  Kamikaze", icon = 3,
            blurb = "Precision with consequences: pop balloons, bomb bullseyes with water balloons, then fly kamikaze runs into the demolition lot." },
        ["Agent"] = new Look { accent = new Color(0.35f, 1f, 0.62f), tagline = "Autopilot  ·  Take-over", icon = 4,
            blurb = "Watch the autopilot fly the mission and take the sticks whenever you want. A calm way to learn the course and the cameras." },
        ["Free flight"] = new Look { accent = new Color(0.85f, 0.85f, 0.9f), tagline = "No score  ·  No rules", icon = 5,
            blurb = "The whole field, every camera, unlimited packs. Just fly." },
    };
    static readonly Look DefaultLook = new Look { accent = Color.white, tagline = "", blurb = "", icon = 0 };
    static Look LookFor(string name) => Looks.TryGetValue(name, out var l) ? l : DefaultLook;

    // ---- state ------------------------------------------------------------------
    int selDiscipline, selDrill, selOption;
    float fade = 1f, fadeTarget;
    float stateTime;
    int pendingLesson = -2;              // lesson to start once the launch fade is black (-1 = free flight)
    bool launchFromMenu;                 // keep the showcase camera up while fading out of a menu
    float resultsAt = -1f;
    LessonManager.Result result;
    int lastResultSerial;
    int starsPlayed;
    float titleT;

    // ---- textures & styles --------------------------------------------------
    Texture2D white, glow, circle, ring, gradient, gradientTop;
    readonly Dictionary<int, GUIStyle> styles = new Dictionary<int, GUIStyle>();
    readonly List<Rect> cardRects = new List<Rect>();
    readonly List<Rect> rowRects = new List<Rect>();
    readonly List<Rect> optionRects = new List<Rect>();
    Rect backRect;                 // on-screen Back button (menus are mouse-driven too, not just ESC)
    float U => Screen.height / 1080f;
    Vector2 mouseGui;
    bool mouseClick, guiClick;

    // ---- navigation input ---------------------------------------------------------
    bool navLeft, navRight, navUp, navDown, confirm, back, startKey, freeKey, esc, langKey;
    int digit = -1;
    float stickRepeatAt;
    int stickDir;
    string queuedNav;

    /// <summary>Injects one navigation press (left|right|up|down|confirm|back|start|f|0-9) - used by DevRemote.</summary>
    public void Nav(string key) => queuedNav = key;

    void Start()
    {
        EnsureTextures();
        var args = System.Environment.GetCommandLineArgs();
        if (System.Array.IndexOf(args, "-droneSelfTest") >= 0) skipMenu = true;
        if (lessons != null) { lessons.autoStartFirstLesson = false; lessons.OnMenuRequested = () => OpenDrills(); lessons.pauseHandledExternally = true; }
        if (skipMenu) { SetState(State.Playing); StartLessonNow(-1); fade = 0f; }
        else { ParkDrone(); selOption = 0; SetState(State.Title); fadeTarget = 0f; }
        Cursor.visible = true;
    }

    // =====================================================================================
    // Update: state machine
    // =====================================================================================

    void Update()
    {
        ReadNav();
        stateTime += Time.unscaledDeltaTime;
        fade = Mathf.MoveTowards(fade, fadeTarget, Time.unscaledDeltaTime / 0.45f);
        if (input != null) input.UiCapturesInput = CapturesInput || (osd != null && osd.SettingsOpen);
        ApplyCameras();

        // a drill just finished -> results card (after the toast/firework moment)
        if (lessons != null && lessons.ResultSerial != lastResultSerial)
        {
            lastResultSerial = lessons.ResultSerial;
            result = lessons.Last;
            resultsAt = Time.unscaledTime + 1.6f;
        }
        if (resultsAt > 0f && Time.unscaledTime >= resultsAt && (Current == State.Playing || Current == State.Intro))
        {
            resultsAt = -1f;
            starsPlayed = 0;
            selOption = 0;
            // Freeze the world behind the card: the menu owns the sticks now, so an
            // unpaused quad would simply drop out of the sky while you read your score.
            if (lessons != null && !lessons.Paused) lessons.TogglePause();
            SetState(State.Results);
            Fx.PlayConfirm();
        }

        if (osd != null && osd.SettingsOpen) return; // the settings panel owns the keys

        switch (Current)
        {
            case State.Title: UpdateTitle(); break;
            case State.Disciplines: UpdateDisciplines(); break;
            case State.Drills: UpdateDrills(); break;
            case State.Launching: UpdateLaunching(); break;
            case State.Intro: if (stateTime > 2.6f) SetState(State.Playing); UpdatePlaying(); break;
            case State.Playing: UpdatePlaying(); break;
            case State.Paused: UpdatePaused(); break;
            case State.Results: UpdateResults(); break;
        }
    }

    void SetState(State s)
    {
        backRect = new Rect();     // the new page draws its own
        Current = s;
        stateTime = 0f;
        mouseClick = false;
    }

    void ApplyCameras()
    {
        bool menuCam = Current == State.Title || Current == State.Disciplines || Current == State.Drills || (Current == State.Launching && launchFromMenu);
        if (showcaseCamera != null && showcaseCamera.enabled != menuCam)
        {
            showcaseCamera.enabled = menuCam;
            var l = showcaseCamera.GetComponent<AudioListener>();
            if (l != null) l.enabled = menuCam;
        }
        if (cameras != null && cameras.suppressed != menuCam) { cameras.suppressed = menuCam; cameras.Apply(); }
    }

    // ---- title ----------------------------------------------------------------------
    static readonly string[] TitleOptions = { "Start training", "Free flight", "Settings", "Quit" };

    void UpdateTitle()
    {
        titleT += Time.unscaledDeltaTime;
        if (langKey) { Loc.Toggle(); Fx.PlayTick(); }
        NavigateOptions(TitleOptions.Length);
        if (startKey) { selOption = 0; confirm = true; }
        if (confirm || ClickedOption())
        {
            Fx.PlayConfirm();
            switch (TitleOptions[selOption])
            {
                case "Start training": selDiscipline = Mathf.Clamp(RememberedDiscipline(), 0, CardCount - 1); SetState(State.Disciplines); break;
                case "Free flight": Launch(-1); break;
                case "Settings": osd?.ToggleSettings(); break;
                case "Quit": QuitApp(); break;
            }
        }
    }

    static void QuitApp()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---- disciplines ---------------------------------------------------------------
    int CardCount => (lessons != null ? lessons.Disciplines.Count : 0) + 1; // + free flight
    bool IsFreeFlightCard(int i) => i == CardCount - 1;

    void UpdateDisciplines()
    {
        int before = selDiscipline;
        if (navLeft) selDiscipline--;
        if (navRight) selDiscipline++;
        if (digit >= 1 && digit <= CardCount) selDiscipline = digit - 1;
        selDiscipline = (selDiscipline + CardCount) % CardCount;
        for (int i = 0; i < cardRects.Count; i++) if (cardRects[i].Contains(mouseGui)) selDiscipline = i;
        if (selDiscipline != before) Fx.PlayTick();

        bool clickedCard = mouseClick && selDiscipline < cardRects.Count && cardRects[selDiscipline].Contains(mouseGui);
        if (freeKey) { Fx.PlayConfirm(); Launch(-1); }
        else if (confirm || clickedCard || (digit >= 1 && digit <= CardCount))
        {
            Fx.PlayConfirm();
            if (IsFreeFlightCard(selDiscipline)) Launch(-1);
            else
            {
                var d = lessons.Disciplines[selDiscipline];
                selDrill = 0;
                for (int i = 0; i < d.lessons.Count; i++) if (lessons.Lessons[d.lessons[i]].BestStars < 3) { selDrill = i; break; }
                SetState(State.Drills);
            }
        }
        else if (back || ClickedBack()) { Fx.PlayTick(); selOption = 0; SetState(State.Title); }
    }

    // ---- drills -------------------------------------------------------------------
    void UpdateDrills()
    {
        var d = lessons.Disciplines[selDiscipline];
        int before = selDrill, beforeD = selDiscipline;
        if (navUp) selDrill--;
        if (navDown) selDrill++;
        if (digit >= 1 && digit <= d.lessons.Count) selDrill = digit - 1;
        if (navLeft) { selDiscipline = (selDiscipline - 1 + CardCount - 1) % (CardCount - 1); selDrill = 0; }
        if (navRight) { selDiscipline = (selDiscipline + 1) % (CardCount - 1); selDrill = 0; }
        d = lessons.Disciplines[selDiscipline];
        selDrill = (selDrill + d.lessons.Count) % d.lessons.Count;
        for (int i = 0; i < rowRects.Count; i++) if (rowRects[i].Contains(mouseGui)) selDrill = i;
        if (selDrill != before || selDiscipline != beforeD) Fx.PlayTick();

        bool clickedRow = mouseClick && selDrill < rowRects.Count && rowRects[selDrill].Contains(mouseGui);
        if (confirm || clickedRow || (digit >= 1 && digit <= d.lessons.Count)) { Fx.PlayConfirm(); Launch(d.lessons[selDrill]); }
        else if (back || ClickedBack()) { Fx.PlayTick(); SetState(State.Disciplines); }
    }

    // ---- launch ------------------------------------------------------------------
    void Launch(int lessonIndex)
    {
        pendingLesson = lessonIndex;
        if (lessons != null && lessons.Paused) lessons.TogglePause();
        launchFromMenu = Current == State.Disciplines || Current == State.Drills || Current == State.Title;
        fadeTarget = 1f;
        SetState(State.Launching);
        Fx.PlayWhoosh();
        if (lessonIndex >= 0) PlayerPrefs.SetInt("menu.lastDiscipline", selDiscipline);
    }

    void UpdateLaunching()
    {
        if (fade < 0.999f) return;
        StartLessonNow(pendingLesson);
        pendingLesson = -2;
        fadeTarget = 0f;
        SetState(State.Intro);
    }

    void StartLessonNow(int index)
    {
        if (lessons == null) return;
        if (lessons.Paused) lessons.TogglePause();
        lessons.enabled = true;
        lessons.StartLesson(index);
        resultsAt = -1f;
        lastResultSerial = lessons.ResultSerial;
        ApplyCameras();
        if (cameras != null) { cameras.suppressed = false; cameras.Apply(); }
    }

    // ---- playing / paused ----------------------------------------------------------
    void UpdatePlaying()
    {
        if (esc || (input != null && (input.PausePressed || input.ScenarioKey == 0))) OpenPause();
    }

    void OpenPause()
    {
        if (Current == State.Paused) return;
        selOption = 0;
        if (lessons != null && !lessons.Paused) lessons.TogglePause();
        SetState(State.Paused);
        Fx.PlayTick();
    }

    string[] PauseOptions()
    {
        bool hasLesson = lessons != null && lessons.Current != null;
        var list = new List<string> { "Resume" };
        if (hasLesson) { list.Add("Retry drill"); if (HasNextDrill()) list.Add("Next drill"); }
        list.Add("Choose drill");
        list.Add("Main menu");
        return list.ToArray();
    }

    void UpdatePaused()
    {
        var opts = PauseOptions();
        NavigateOptions(opts.Length);
        if (back) { Resume(); return; }
        if (confirm || ClickedOption()) { Fx.PlayConfirm(); DoOption(opts[selOption]); }
    }

    void Resume()
    {
        if (lessons != null && lessons.Paused) lessons.TogglePause();
        SetState(State.Playing);
        Fx.PlayTick();
    }

    // ---- results ---------------------------------------------------------------------
    string[] ResultOptions()
    {
        var list = new List<string>();
        if (HasNextDrill()) list.Add("Next drill");
        list.Add("Retry");
        list.Add("Choose drill");
        list.Add("Main menu");
        return list.ToArray();
    }

    void UpdateResults()
    {
        var opts = ResultOptions();
        NavigateOptions(opts.Length);
        // stars chime in one by one
        int visible = Mathf.Clamp(Mathf.FloorToInt((stateTime - 0.5f) / 0.45f) + 1, 0, result.stars);
        while (starsPlayed < visible) { Fx.PlayStar(starsPlayed); starsPlayed++; }
        if (stateTime < 0.8f) return;
        if (back) { Fx.PlayTick(); OpenDrills(); return; }
        if (confirm || ClickedOption()) { Fx.PlayConfirm(); DoOption(opts[selOption]); }
    }

    bool HasNextDrill()
    {
        if (lessons == null) return false;
        var d = lessons.CurrentDiscipline;
        return d != null && d.lessons.IndexOf(lessons.CurrentIndex) + 1 < d.lessons.Count;
    }

    void DoOption(string opt)
    {
        switch (opt)
        {
            case "Resume": Resume(); break;
            case "Retry drill":
            case "Retry": if (lessons.Paused) lessons.TogglePause(); Launch(lessons.CurrentIndex); break;
            case "Next drill":
                {
                    var d = lessons.CurrentDiscipline;
                    int pos = d.lessons.IndexOf(lessons.CurrentIndex);
                    if (lessons.Paused) lessons.TogglePause();
                    Launch(d.lessons[pos + 1]);
                    break;
                }
            case "Choose drill": OpenDrills(); break;
            case "Main menu": OpenTitle(); break;
        }
    }

    /// <summary>Back to the drill list of the current discipline (used by Esc from the results and by 0 in flight).</summary>
    public void OpenDrills()
    {
        bool inDiscipline = false;
        if (lessons != null)
        {
            if (lessons.Paused) lessons.TogglePause();
            var cur = lessons.CurrentDiscipline;
            inDiscipline = cur != null;
            selDiscipline = Mathf.Clamp(inDiscipline ? lessons.Disciplines.IndexOf(cur) : RememberedDiscipline(), 0, CardCount - 1);
            selDrill = inDiscipline ? Mathf.Max(0, cur.lessons.IndexOf(lessons.CurrentIndex)) : 0;
            ParkDrone();
        }
        pendingLesson = -2;
        SetState(inDiscipline ? State.Drills : State.Disciplines);
    }

    public void OpenTitle()
    {
        if (lessons != null && lessons.Paused) lessons.TogglePause();
        ParkDrone();
        pendingLesson = -2;
        selOption = 0;
        SetState(State.Title);
    }

    /// <summary>Ends the current drill, disarms and puts the drone back on the pad while menus are up.</summary>
    void ParkDrone()
    {
        if (lessons == null) return;
        lessons.StartLesson(-1);
        lessons.enabled = false;
        flightController?.Disarm();
    }

    int RememberedDiscipline() => PlayerPrefs.GetInt("menu.lastDiscipline", 0);

    /// <summary>Called by DevRemote: play a lesson directly (bypasses the menus).</summary>
    public void PlayLesson(int index)
    {
        if (lessons != null && lessons.Paused) lessons.TogglePause();
        StartLessonNow(index);
        fade = 0f; fadeTarget = 0f;
        SetState(State.Playing);
    }

    // ---- option lists (pause / results) ----------------------------------------------
    void NavigateOptions(int count)
    {
        int before = selOption;
        if (navUp) selOption--;
        if (navDown) selOption++;
        selOption = (selOption + count) % count;
        for (int i = 0; i < optionRects.Count && i < count; i++) if (optionRects[i].Contains(mouseGui)) selOption = i;
        if (selOption != before) Fx.PlayTick();
    }

    /// <summary>True when the on-screen Back button was clicked this frame.</summary>
    bool ClickedBack() => mouseClick && backRect.width > 0f && backRect.Contains(mouseGui);

    /// <summary>Draws the Back button top-left and remembers its hit area.</summary>
    void DrawBack(string label)
    {
        float bh = 48f * U, bw = Mathf.Max(190f * U, S(20, FontStyle.Bold).CalcSize(new GUIContent(Loc.T(label))).x + 92f * U);
        backRect = new Rect(34f * U, 26f * U, bw, bh);
        bool hot = backRect.Contains(mouseGui);
        Rounded(backRect, new Color(0.05f, 0.06f, 0.09f, hot ? 0.95f : 0.8f), 12f * U);
        Rounded(backRect, new Color(1, 1, 1, hot ? 0.75f : 0.35f), 12f * U, 2f * U);
        Text(backRect, "←   " + Loc.T(label), S(20, FontStyle.Bold, TextAnchor.MiddleCenter), hot ? Color.white : new Color(1, 1, 1, 0.85f), false);
    }

    bool ClickedOption() => mouseClick && selOption < optionRects.Count && optionRects[selOption].Contains(mouseGui);

    // ---- input -------------------------------------------------------------------------
    void ReadNav()
    {
        navLeft = navRight = navUp = navDown = confirm = back = startKey = freeKey = esc = langKey = mouseClick = false;
        digit = -1;
        var kb = Keyboard.current;
        var gp = Gamepad.current;
        var ms = Mouse.current;
        if (kb != null)
        {
            navLeft |= kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame;
            navRight |= kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame;
            navUp |= kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame;
            navDown |= kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame;
            confirm |= kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
            back |= kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame;
            esc |= kb.escapeKey.wasPressedThisFrame;
            KeyControl[] digits = { kb.digit0Key, kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key, kb.digit5Key, kb.digit6Key, kb.digit7Key, kb.digit8Key, kb.digit9Key };
            for (int i = 0; i < digits.Length; i++) if (digits[i].wasPressedThisFrame) digit = i;
            freeKey |= kb.fKey.wasPressedThisFrame;
            langKey |= kb.lKey.wasPressedThisFrame;
        }
        if (gp != null)
        {
            navLeft |= gp.dpad.left.wasPressedThisFrame;
            navRight |= gp.dpad.right.wasPressedThisFrame;
            navUp |= gp.dpad.up.wasPressedThisFrame;
            navDown |= gp.dpad.down.wasPressedThisFrame;
            confirm |= gp.buttonSouth.wasPressedThisFrame;
            back |= gp.buttonEast.wasPressedThisFrame;
            startKey |= gp.startButton.wasPressedThisFrame;
            esc |= gp.startButton.wasPressedThisFrame;
            Vector2 st = gp.leftStick.ReadValue();
            int dir = st.x < -0.6f ? 1 : st.x > 0.6f ? 2 : st.y > 0.6f ? 3 : st.y < -0.6f ? 4 : 0;
            if (dir != stickDir) { stickDir = dir; stickRepeatAt = Time.unscaledTime + 0.35f; Pulse(dir); }
            else if (dir != 0 && Time.unscaledTime >= stickRepeatAt) { stickRepeatAt = Time.unscaledTime + 0.18f; Pulse(dir); }
        }
        if (ms != null)
        {
            Vector2 p = ms.position.ReadValue();
            mouseGui = new Vector2(p.x, Screen.height - p.y);
            mouseClick = ms.leftButton.wasPressedThisFrame;
        }
        if (guiClick) { mouseClick = true; guiClick = false; }
        if (CapturesInput && ms != null && ms.delta.ReadValue().sqrMagnitude > 0f) Cursor.visible = true;
        if (queuedNav != null)
        {
            switch (queuedNav)
            {
                case "left": navLeft = true; break;
                case "right": navRight = true; break;
                case "up": navUp = true; break;
                case "down": navDown = true; break;
                case "confirm": confirm = true; break;
                case "back": back = true; break;
                case "esc": esc = true; back = true; break;
                case "start": startKey = true; break;
                case "f": freeKey = true; break;
                case "l": langKey = true; break;
                default: if (int.TryParse(queuedNav, out int d)) digit = d; break;
            }
            queuedNav = null;
        }
    }

    void Pulse(int dir)
    {
        if (dir == 1) navLeft = true; else if (dir == 2) navRight = true; else if (dir == 3) navUp = true; else if (dir == 4) navDown = true;
    }

    // =====================================================================================
    // Drawing
    // =====================================================================================

    void EnsureTextures()
    {
        if (white == null) white = Texture2D.whiteTexture;
        if (glow == null) glow = Radial(128, 0.0f, 1.0f, 2.2f);
        if (circle == null) circle = Radial(64, 0.86f, 1.0f, 1f);
        if (ring == null) ring = Ring(96);
        if (gradient == null) gradient = Gradient(64, false);
        if (gradientTop == null) gradientTop = Gradient(64, true);
    }

    void OnGUI()
    {
        EnsureTextures();
        var ev = Event.current;
        if (ev != null && ev.type == EventType.MouseDown && ev.button == 0) { guiClick = true; mouseGui = ev.mousePosition; }
        else if (ev != null && ev.type == EventType.MouseMove) mouseGui = ev.mousePosition;
        float w = Screen.width, h = Screen.height;
        // While Settings is open, Osd draws only the settings panel over a menu state (see Osd.OnGUI),
        // so our own page must not draw its foreground content too or the two pages appear stacked.
        bool settingsOpen = osd != null && osd.SettingsOpen;
        switch (Current)
        {
            case State.Title: DrawBackdrop(w, h, 0.55f); if (!settingsOpen) DrawTitle(w, h); break;
            case State.Disciplines: DrawBackdrop(w, h, 0.62f); if (!settingsOpen) DrawDisciplines(w, h); break;
            case State.Drills: DrawBackdrop(w, h, 0.8f); if (!settingsOpen) DrawDrills(w, h); break;
            case State.Intro: DrawIntro(w, h); break;
            case State.Paused: DrawDim(w, h, 0.55f); if (!settingsOpen) DrawOptionPanel(w, h, Loc.T("PAUSED"), CurrentDrillLabel(), PauseOptions(), LookFor(lessons?.Current?.discipline ?? "Free flight").accent); break;
            case State.Results: DrawDim(w, h, 0.45f); if (!settingsOpen) DrawResults(w, h); break;
            case State.Launching: if (launchFromMenu) DrawBackdrop(w, h, 0.7f); break;
        }
        if (fade > 0.001f) { GUI.color = new Color(0, 0, 0, fade); GUI.DrawTexture(new Rect(0, 0, w, h), white); GUI.color = Color.white; }
    }

    string CurrentDrillLabel()
    {
        var l = lessons?.Current;
        return l == null ? Loc.T("Free flight") : Loc.T(l.discipline) + "  ·  " + l.DisplayTitle;
    }

    // ---- shared drawing helpers ------------------------------------------------------------
    GUIStyle S(int px, FontStyle fs = FontStyle.Normal, TextAnchor anchor = TextAnchor.UpperLeft, bool wrap = false)
    {
        int size = Mathf.Max(9, Mathf.RoundToInt(px * U));
        int key = size * 16 + (int)fs * 4 + (int)anchor % 4 + (wrap ? 1000000 : 0) + ((int)anchor / 4) * 100000;
        if (!styles.TryGetValue(key, out var st))
        {
            st = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fs, alignment = anchor, richText = true, wordWrap = wrap, clipping = TextClipping.Overflow };
            st.normal.textColor = Color.white;
            styles[key] = st;
        }
        return st;
    }

    void Rounded(Rect r, Color c, float radius, float border = 0f)
    {
        GUI.DrawTexture(r, white, ScaleMode.StretchToFill, true, 0f, c, border, radius);
    }

    void Text(Rect r, string s, GUIStyle st, Color c, bool shadow = true)
    {
        if (shadow)
        {
            GUI.color = new Color(0, 0, 0, 0.7f * c.a);
            GUI.Label(new Rect(r.x + 2f * U, r.y + 2f * U, r.width, r.height), s, st);
        }
        GUI.color = c;
        GUI.Label(r, s, st);
        GUI.color = Color.white;
    }

    void Glow(Vector2 centre, float radius, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(new Rect(centre.x - radius, centre.y - radius, radius * 2f, radius * 2f), glow);
        GUI.color = Color.white;
    }

    void DrawBackdrop(float w, float h, float strength)
    {
        // darken the live camera: vignette-ish gradient at top and bottom plus a slow light sweep
        GUI.color = new Color(0, 0, 0, strength * 0.55f);
        GUI.DrawTexture(new Rect(0, 0, w, h), white);
        GUI.color = new Color(0, 0, 0, strength);
        GUI.DrawTexture(new Rect(0, h * 0.45f, w, h * 0.55f), gradient);
        GUI.color = new Color(0, 0, 0, strength * 0.85f);
        GUI.DrawTexture(new Rect(0, 0, w, h * 0.4f), gradientTop);
        float sweep = Mathf.Repeat(Time.unscaledTime * 0.08f, 1.4f) - 0.2f;
        GUI.color = new Color(1, 1, 1, 0.04f);
        GUI.DrawTexture(new Rect(w * sweep - w * 0.08f, 0, w * 0.16f, h), glow);
        GUI.color = Color.white;
    }

    void DrawDim(float w, float h, float a)
    {
        GUI.color = new Color(0, 0, 0, a);
        GUI.DrawTexture(new Rect(0, 0, w, h), white);
        GUI.color = Color.white;
    }

    void Footer(float w, float h, string text)
    {
        Text(new Rect(0, h - 58f * U, w, 40f * U), text, S(22, FontStyle.Normal, TextAnchor.MiddleCenter), new Color(1, 1, 1, 0.65f));
    }

    string Stars(int n, int of = 3) => new string('★', n) + new string('☆', of - n);

    string InputName()
    {
        if (Joystick.current != null) return Joystick.current.displayName;
        if (Gamepad.current != null) return Gamepad.current.displayName;
        return Loc.T("Keyboard");
    }

    // ---- title --------------------------------------------------------------------------
    void DrawTitle(float w, float h)
    {
        float pw = Mathf.Min(720f * U, w - 40f), ph = 578f * U;
        var r = new Rect((w - pw) * 0.5f, (h - ph) * 0.5f, pw, ph);
        Glow(new Vector2(r.center.x, r.y + 110f * U), 520f * U, new Color(0.35f, 0.7f, 1f, 0.18f));
        Rounded(r, new Color(0.05f, 0.06f, 0.09f, 0.84f), 26f * U);
        Rounded(new Rect(r.x, r.y, r.width, 8f * U), new Color(0.4f, 0.75f, 1f), 4f * U);

        // title block
        float ty = r.y + 26f * U;
        var titleStyle = S(74, FontStyle.Bold, TextAnchor.MiddleCenter);
        for (int i = 2; i >= 1; i--)
        {
            GUI.color = new Color(0.4f, 0.75f, 1f, 0.12f);
            GUI.Label(new Rect(r.x - i * 2f * U, ty - i * 2f * U, r.width, 100f * U), "DRONE TRAINER", titleStyle);
            GUI.Label(new Rect(r.x + i * 2f * U, ty + i * 2f * U, r.width, 100f * U), "DRONE TRAINER", titleStyle);
        }
        Text(new Rect(r.x, ty, r.width, 100f * U), "DRONE TRAINER", titleStyle, Color.white);
        Text(new Rect(r.x, ty + 96f * U, r.width, 28f * U), Loc.T("FPV FLIGHT SCHOOL"), S(19, FontStyle.Bold, TextAnchor.MiddleCenter), new Color(0.6f, 0.85f, 1f), false);
        Text(new Rect(r.x + 40f * U, ty + 126f * U, r.width - 80f * U, 44f * U),
            Loc.T("5-inch freestyle quad  ·  Betaflight-style controller  ·  real wind, battery sag and prop strikes"),
            S(14, FontStyle.Normal, TextAnchor.UpperCenter, true), new Color(1, 1, 1, 0.6f), false);

        // discipline chips
        string[] chips = { "Basics", "Racing", "Freestyle", "Demolition", "Agent" };
        float chipW = 118f * U, chipH = 30f * U, gap = 8f * U;
        float x0 = r.center.x - (chips.Length * chipW + (chips.Length - 1) * gap) * 0.5f;
        for (int i = 0; i < chips.Length; i++)
        {
            var look = LookFor(chips[i]);
            var cr = new Rect(x0 + i * (chipW + gap), ty + 178f * U, chipW, chipH);
            float pulse = 0.5f + 0.5f * Mathf.Sin(titleT * 2f + i * 0.9f);
            Rounded(cr, new Color(look.accent.r, look.accent.g, look.accent.b, 0.10f + 0.08f * pulse), 8f * U);
            Rounded(cr, new Color(look.accent.r, look.accent.g, look.accent.b, 0.5f), 8f * U, 1.5f * U);
            Text(cr, Loc.T(chips[i]).ToUpper(), S(13, FontStyle.Bold, TextAnchor.MiddleCenter), look.accent, false);
        }

        // main buttons
        float oh = 52f * U, ogap = 10f * U;
        DrawOptions(new Rect(r.x + 150f * U, ty + 226f * U, r.width - 300f * U, TitleOptions.Length * (oh + ogap)), TitleOptions, new Color(0.4f, 0.75f, 1f), oh, ogap);

        // panel footer: input, language, progress
        int earned = 0, possible = 0, drills = 0;
        if (lessons != null) foreach (var d in lessons.Disciplines) { var p = lessons.Progress(d); earned += p.earned; possible += p.possible; drills += d.lessons.Count; }
        float fy = r.yMax - 60f * U;
        Rounded(new Rect(r.x + 30f * U, fy - 10f * U, r.width - 60f * U, 1f), new Color(1, 1, 1, 0.12f), 0f);
        Text(new Rect(r.x + 30f * U, fy, r.width * 0.5f, 24f * U), $"{Loc.T("Input")}:  <b>{InputName()}</b>", S(14), new Color(1, 1, 1, 0.65f), false);
        Text(new Rect(r.x + r.width * 0.5f, fy, r.width * 0.5f - 30f * U, 24f * U), $"{Loc.T("Pilot progress")}  <b>{earned} / {possible}</b> ★  ·  {Loc.F("{0} drills", drills)}",
            S(14, FontStyle.Normal, TextAnchor.UpperRight), new Color(1, 1, 1, 0.65f), false);
        Text(new Rect(r.x, fy + 26f * U, r.width, 24f * U), $"{Loc.T("Language")}:  <b>{Loc.LangName}</b>   (L)", S(14, FontStyle.Normal, TextAnchor.UpperCenter), new Color(0.6f, 0.85f, 1f), false);

        Footer(w, h, Loc.T("↑  ↓  choose      ENTER / ⓐ  select      L  language      TAB  settings"));
    }

    // ---- disciplines ----------------------------------------------------------------------
    void DrawDisciplines(float w, float h)
    {
        DrawBack("Main menu");
        Text(new Rect(0, 48f * U, w, 60f * U), Loc.T("CHOOSE YOUR TRAINING"), S(52, FontStyle.Bold, TextAnchor.MiddleCenter), Color.white);
        Text(new Rect(0, 108f * U, w, 34f * U), Loc.T("Each discipline is a ladder of scored drills. Three stars on every drill and you are ready for the real thing."),
            S(21, FontStyle.Normal, TextAnchor.MiddleCenter), new Color(1, 1, 1, 0.7f));

        int n = CardCount;
        float gap = 22f * U;
        float cardW = Mathf.Min(300f * U, (w - 80f * U - gap * (n - 1)) / n);
        float cardH = Mathf.Min(h * 0.5f, cardW * 1.55f);
        float x0 = (w - (n * cardW + (n - 1) * gap)) * 0.5f;
        float y0 = h * 0.5f - cardH * 0.5f + 20f * U;
        cardRects.Clear();
        for (int i = 0; i < n; i++)
        {
            bool free = IsFreeFlightCard(i);
            string name = free ? "Free flight" : lessons.Disciplines[i].name;
            var look = LookFor(name);
            bool sel = i == selDiscipline;
            float grow = sel ? 1.07f : 1f;
            var r = new Rect(x0 + i * (cardW + gap), y0, cardW, cardH);
            cardRects.Add(r);
            var rr = new Rect(r.center.x - r.width * grow * 0.5f, r.center.y - r.height * grow * 0.5f, r.width * grow, r.height * grow);

            if (sel) Glow(rr.center, rr.height * 0.85f, new Color(look.accent.r, look.accent.g, look.accent.b, 0.35f));
            Rounded(rr, new Color(0.05f, 0.06f, 0.09f, sel ? 0.92f : 0.75f), 18f * U);
            Rounded(new Rect(rr.x, rr.y, rr.width, 10f * U), look.accent, 5f * U);
            float pulse = sel ? 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 5f) : 0.25f;
            Rounded(rr, new Color(look.accent.r, look.accent.g, look.accent.b, pulse), 18f * U, (sel ? 3f : 1.5f) * U);

            // icon + number
            DrawIcon(look.icon, new Rect(rr.x + rr.width * 0.5f - 60f * U, rr.y + 40f * U, 120f * U, 120f * U), look.accent, sel);
            Text(new Rect(rr.x + 16f * U, rr.y + 16f * U, 60f * U, 40f * U), free ? "F" : (i + 1).ToString(), S(30, FontStyle.Bold), new Color(1, 1, 1, 0.55f));

            Text(new Rect(rr.x, rr.y + 175f * U, rr.width, 44f * U), Loc.T(name).ToUpper(), S(30, FontStyle.Bold, TextAnchor.MiddleCenter), Color.white);
            Text(new Rect(rr.x, rr.y + 218f * U, rr.width, 30f * U), Loc.T(look.tagline), S(17, FontStyle.Normal, TextAnchor.MiddleCenter), look.accent);
            Text(new Rect(rr.x + 18f * U, rr.y + 258f * U, rr.width - 36f * U, 150f * U), Loc.T(look.blurb), S(16, FontStyle.Normal, TextAnchor.UpperCenter, true), new Color(1, 1, 1, sel ? 0.85f : 0.6f), false);

            // progress
            if (!free)
            {
                var d = lessons.Disciplines[i];
                var (earned, possible) = lessons.Progress(d);
                float frac = possible > 0 ? (float)earned / possible : 0f;
                var bar = new Rect(rr.x + 24f * U, rr.yMax - 62f * U, rr.width - 48f * U, 10f * U);
                Rounded(bar, new Color(1, 1, 1, 0.12f), 5f * U);
                if (frac > 0f) Rounded(new Rect(bar.x, bar.y, bar.width * frac, bar.height), look.accent, 5f * U);
                string status = earned >= possible ? Loc.T("COMPLETE") : earned == 0 ? Loc.T("not started") : Loc.F("resume at drill {0}", NextDrillNumber(d));
                Text(new Rect(rr.x, rr.yMax - 48f * U, rr.width, 30f * U), $"{earned} / {possible} ★   ·   {d.lessons.Count} drills   ·   {status}",
                    S(15, FontStyle.Normal, TextAnchor.MiddleCenter), new Color(1, 1, 1, 0.75f), false);
            }
            else
                Text(new Rect(rr.x, rr.yMax - 48f * U, rr.width, 30f * U), Loc.T("sandbox"), S(15, FontStyle.Normal, TextAnchor.MiddleCenter), new Color(1, 1, 1, 0.6f), false);
        }
        Footer(w, h, Loc.T("←  →  choose      ENTER / ⓐ  open      1-5  jump      F  free flight      ESC  back"));
    }

    int NextDrillNumber(LessonManager.Discipline d)
    {
        for (int i = 0; i < d.lessons.Count; i++) if (lessons.Lessons[d.lessons[i]].BestStars < 3) return i + 1;
        return d.lessons.Count;
    }

    // ---- drills -------------------------------------------------------------------------------
    void DrawDrills(float w, float h)
    {
        // OnGUI can run between a state change and the next Update, so never trust the
        // selection indices here - clamp them to what this discipline actually holds.
        if (lessons.Disciplines.Count == 0) return;
        selDiscipline = Mathf.Clamp(selDiscipline, 0, lessons.Disciplines.Count - 1);
        var d = lessons.Disciplines[selDiscipline];
        if (d.lessons.Count == 0) return;
        selDrill = Mathf.Clamp(selDrill, 0, d.lessons.Count - 1);
        var look = LookFor(d.name);
        DrawBack("Disciplines");
        float margin = 60f * U, top = 88f * U;   // leaves room for the Back button above the panels
        float leftW = w * 0.32f, gap = 30f * U;
        float rightX = margin + leftW + gap, rightW = w - rightX - margin;
        float panelH = h - top - 110f * U;

        // left: discipline card
        var left = new Rect(margin, top, leftW, panelH);
        Glow(new Vector2(left.x + left.width * 0.5f, left.y + 140f * U), 320f * U, new Color(look.accent.r, look.accent.g, look.accent.b, 0.25f));
        Rounded(left, new Color(0.05f, 0.06f, 0.09f, 0.9f), 20f * U);
        Rounded(new Rect(left.x, left.y, left.width, 10f * U), look.accent, 5f * U);
        DrawIcon(look.icon, new Rect(left.x + left.width * 0.5f - 70f * U, left.y + 40f * U, 140f * U, 140f * U), look.accent, true);
        Text(new Rect(left.x, left.y + 195f * U, left.width, 56f * U), Loc.T(d.name).ToUpper(), S(44, FontStyle.Bold, TextAnchor.MiddleCenter), Color.white);
        Text(new Rect(left.x, left.y + 250f * U, left.width, 30f * U), Loc.T(look.tagline), S(19, FontStyle.Normal, TextAnchor.MiddleCenter), look.accent);
        Text(new Rect(left.x + 28f * U, left.y + 300f * U, left.width - 56f * U, 200f * U), Loc.T(look.blurb), S(18, FontStyle.Normal, TextAnchor.UpperLeft, true), new Color(1, 1, 1, 0.8f), false);
        int attempts = 0, done = 0, bestScoreSum = 0;
        foreach (var li in d.lessons) { var ll = lessons.Lessons[li]; attempts += ll.Attempts; if (ll.BestStars > 0) done++; bestScoreSum += ll.BestScore; }
        string[] statLabels = { Loc.T("DRILLS"), Loc.T("PASSED"), Loc.T("ATTEMPTS"), Loc.T("AVG BEST") };
        string[] statValues = { d.lessons.Count.ToString(), done + " / " + d.lessons.Count, attempts.ToString(), done > 0 ? (bestScoreSum / d.lessons.Count).ToString() : "-" };
        float statY = left.yMax - 300f * U, statW = (left.width - 56f * U) / 2f, statH = 70f * U;
        for (int i = 0; i < 4; i++)
        {
            var sr = new Rect(left.x + 28f * U + (i % 2) * statW, statY + (i / 2) * (statH + 10f * U), statW - 10f * U, statH);
            Rounded(sr, new Color(1, 1, 1, 0.06f), 10f * U);
            Text(new Rect(sr.x, sr.y + 8f * U, sr.width, 20f * U), statLabels[i], S(12, FontStyle.Bold, TextAnchor.MiddleCenter), new Color(1, 1, 1, 0.5f), false);
            Text(new Rect(sr.x, sr.y + 26f * U, sr.width, 40f * U), statValues[i], S(26, FontStyle.Bold, TextAnchor.MiddleCenter), Color.white, false);
        }
        var (earned, possible) = lessons.Progress(d);
        float frac = possible > 0 ? (float)earned / possible : 0f;
        var bar = new Rect(left.x + 28f * U, left.yMax - 90f * U, left.width - 56f * U, 12f * U);
        Rounded(bar, new Color(1, 1, 1, 0.12f), 6f * U);
        if (frac > 0f) Rounded(new Rect(bar.x, bar.y, bar.width * frac, bar.height), look.accent, 6f * U);
        Text(new Rect(left.x, left.yMax - 70f * U, left.width, 40f * U), Loc.F("{0} / {1} ★  earned", earned, possible), S(22, FontStyle.Bold, TextAnchor.MiddleCenter), Color.white, false);
        if (d.name == "Racing" && PlayerPrefs.HasKey("race.bestlap"))
            Text(new Rect(left.x, left.yMax - 40f * U, left.width, 30f * U), Loc.F("best lap  {0:0.00} s", PlayerPrefs.GetFloat("race.bestlap")), S(17, FontStyle.Normal, TextAnchor.MiddleCenter), look.accent, false);

        // right: drill rows
        Text(new Rect(rightX, top - 4f * U, rightW, 40f * U), Loc.T("DRILLS"), S(26, FontStyle.Bold), new Color(1, 1, 1, 0.6f));
        float rowH = Mathf.Min(96f * U, (panelH - 200f * U) / Mathf.Max(1, d.lessons.Count) - 8f * U);
        float y = top + 40f * U;
        rowRects.Clear();
        for (int i = 0; i < d.lessons.Count; i++)
        {
            var l = lessons.Lessons[d.lessons[i]];
            bool sel = i == selDrill;
            var r = new Rect(rightX, y, rightW, rowH);
            rowRects.Add(r);
            Rounded(r, new Color(0.05f, 0.06f, 0.09f, 0.9f), 14f * U);
            if (sel) { Rounded(r, new Color(look.accent.r, look.accent.g, look.accent.b, 0.22f), 14f * U); Rounded(r, new Color(look.accent.r, look.accent.g, look.accent.b, 0.85f), 14f * U, 2.5f * U); }
            Rounded(new Rect(r.x, r.y + 12f * U, 6f * U, r.height - 24f * U), l.BestStars >= 3 ? look.accent : new Color(1, 1, 1, 0.15f), 3f * U);

            Text(new Rect(r.x + 24f * U, r.y, 60f * U, r.height), (i + 1).ToString("00"), S(30, FontStyle.Bold, TextAnchor.MiddleLeft), new Color(1, 1, 1, 0.4f), false);
            Text(new Rect(r.x + 84f * U, r.y + 10f * U, r.width * 0.55f, r.height * 0.5f), l.DisplayTitle, S(27, FontStyle.Bold, TextAnchor.MiddleLeft), Color.white, false);
            string meta = Loc.T(l.venue) + "   ·   " + l.recommendedMode.ToString().ToUpper() + "   ·   " + Loc.F("par {0:0} s", l.parTime) + "   ·   " + (l.Attempts == 0 ? Loc.T("never flown") : Loc.F(l.Attempts == 1 ? "{0} attempt" : "{0} attempts", l.Attempts));
            Text(new Rect(r.x + 84f * U, r.y + r.height * 0.52f, r.width * 0.6f, r.height * 0.4f), meta, S(15, FontStyle.Normal, TextAnchor.MiddleLeft), new Color(1, 1, 1, 0.55f), false);

            // mode chip
            var chip = new Rect(r.xMax - 250f * U, r.y + r.height * 0.5f - 15f * U, 80f * U, 30f * U);
            var chipCol = l.recommendedMode == FlightMode.Acro ? new Color(1f, 0.45f, 0.35f) : l.recommendedMode == FlightMode.Horizon ? new Color(1f, 0.8f, 0.3f) : new Color(0.4f, 0.8f, 1f);
            Rounded(chip, new Color(chipCol.r, chipCol.g, chipCol.b, 0.2f), 8f * U);
            Text(chip, l.recommendedMode.ToString().ToUpper(), S(13, FontStyle.Bold, TextAnchor.MiddleCenter), chipCol, false);

            Text(new Rect(r.xMax - 150f * U, r.y, 130f * U, r.height), Stars(l.BestStars), S(32, FontStyle.Normal, TextAnchor.MiddleRight), l.BestStars > 0 ? new Color(1f, 0.85f, 0.3f) : new Color(1, 1, 1, 0.3f), false);
            if (l.BestScore > 0) Text(new Rect(r.xMax - 150f * U, r.y + r.height * 0.62f, 130f * U, r.height * 0.35f), Loc.F("best {0}", l.BestScore), S(13, FontStyle.Normal, TextAnchor.MiddleRight), new Color(1, 1, 1, 0.5f), false);
            y += rowH + 8f * U;
        }

        // Student briefing for the selected drill: goal, steps, skill, tip, controls, scoring
        var selLesson = lessons.Lessons[d.lessons[selDrill]];
        var info = new Rect(rightX, y + 10f * U, rightW, top + panelH - y - 10f * U);
        Rounded(info, new Color(0.05f, 0.06f, 0.09f, 0.88f), 14f * U);
        float bx = info.x + 22f * U, bw = info.width - 44f * U, by = info.y + 14f * U;

        // header row: BRIEFING + difficulty dots
        Text(new Rect(bx, by, bw * 0.5f, 26f * U), Loc.T("BRIEFING"), S(16, FontStyle.Bold), look.accent, false);
        string dots = "";
        for (int i = 0; i < 5; i++) dots += i < selLesson.difficulty ? "●" : "○";
        Text(new Rect(bx, by, bw, 26f * U), Loc.T("Difficulty") + "  " + dots, S(16, FontStyle.Bold, TextAnchor.UpperRight), new Color(1, 1, 1, 0.75f), false);
        by += 30f * U;

        // goal
        if (!string.IsNullOrEmpty(selLesson.goal))
        {
            Text(new Rect(bx, by, bw, 60f * U), selLesson.DisplayGoal, S(21, FontStyle.Bold, TextAnchor.UpperLeft, true), Color.white, false);
            by += Mathf.Max(30f * U, S(21, FontStyle.Bold, TextAnchor.UpperLeft, true).CalcHeight(new GUIContent(selLesson.DisplayGoal), bw)) + 10f * U;
        }

        // numbered steps
        if (selLesson.steps != null)
            for (int i = 0; i < selLesson.steps.Length; i++)
            {
                string step = Loc.T(selLesson.steps[i]);
                var stStyle = S(18, FontStyle.Normal, TextAnchor.UpperLeft, true);
                float sh = Mathf.Max(24f * U, stStyle.CalcHeight(new GUIContent(step), bw - 34f * U));
                Text(new Rect(bx, by, 28f * U, 24f * U), (i + 1) + ".", S(18, FontStyle.Bold), look.accent, false);
                Text(new Rect(bx + 30f * U, by, bw - 34f * U, sh), step, stStyle, new Color(1, 1, 1, 0.92f), false);
                by += sh + 4f * U;
            }
        by += 8f * U;

        // skill / tip / controls, each on one line with a coloured label
        void Note(string label, string body, Color col)
        {
            if (string.IsNullOrEmpty(body)) return;
            var bodyStyle = S(17, FontStyle.Normal, TextAnchor.UpperLeft, true);
            var labelStyle = S(14, FontStyle.Bold);
            string labelText = Loc.T(label);
            // the label column has to fit the translated word, not a fixed English width
            float lw = Mathf.Max(120f * U, labelStyle.CalcSize(new GUIContent(labelText)).x + 14f * U);
            float bh = Mathf.Max(22f * U, bodyStyle.CalcHeight(new GUIContent(body), bw - lw));
            Text(new Rect(bx, by, lw, 22f * U), labelText, labelStyle, col, false);
            Text(new Rect(bx + lw, by, bw - lw, bh), body, bodyStyle, new Color(1, 1, 1, 0.82f), false);
            by += bh + 6f * U;
        }
        Note("YOU LEARN", selLesson.DisplaySkill, new Color(0.5f, 0.9f, 1f));
        Note("WATCH OUT", selLesson.DisplayTip, new Color(1f, 0.8f, 0.35f));
        Note("CONTROLS", selLesson.DisplayControls, new Color(0.75f, 0.85f, 0.95f));

        // scoring rules + best result, pinned to the bottom of the card
        float ry = info.yMax - 62f * U;
        Text(new Rect(bx, ry, bw, 24f * U),
            Loc.F("100 points, minus {0} per crash and {1:0.#} per second over par ({2:0} s).   ★ 35   ★★ 65   ★★★ 90", selLesson.pointsPerCrash, selLesson.pointsPerSecondOverPar, selLesson.parTime),
            S(15), new Color(1, 1, 1, 0.6f), false);
        string bestLine = selLesson.Attempts == 0 ? Loc.T("You have not flown this drill yet.") : Loc.F("Your best: {0}   score {1}   over {2} attempt(s)", Stars(selLesson.BestStars), selLesson.BestScore, selLesson.Attempts);
        Text(new Rect(bx, ry + 26f * U, bw, 26f * U), bestLine, S(16, FontStyle.Bold), new Color(1f, 0.85f, 0.3f), false);

        Footer(w, h, Loc.T("↑  ↓  drill      ←  →  discipline      ENTER / ⓐ  FLY      ESC  back"));
    }

    // ---- launch intro --------------------------------------------------------------------------
    void DrawIntro(float w, float h)
    {
        float a = stateTime < 0.4f ? stateTime / 0.4f : stateTime > 2.0f ? Mathf.Clamp01((2.6f - stateTime) / 0.6f) : 1f;
        var l = lessons?.Current;
        var look = LookFor(l != null ? l.discipline : "Free flight");
        float y = h * 0.12f;
        Glow(new Vector2(w * 0.5f, y + 50f * U), 420f * U, new Color(look.accent.r, look.accent.g, look.accent.b, 0.25f * a));
        Text(new Rect(0, y, w, 40f * U), l != null ? Loc.T(l.discipline).ToUpper() + "   ·   " + Loc.T(l.venue).ToUpper() : Loc.T("FREE FLIGHT"), S(24, FontStyle.Bold, TextAnchor.MiddleCenter), new Color(look.accent.r, look.accent.g, look.accent.b, a));
        Text(new Rect(0, y + 36f * U, w, 90f * U), l != null ? l.DisplayTitle : Loc.T("The whole field is yours"), S(64, FontStyle.Bold, TextAnchor.MiddleCenter), new Color(1, 1, 1, a));
        if (l != null)
        {
            // the goal, so a student reads what to do while the scene fades in
            Text(new Rect(w * 0.15f, y + 126f * U, w * 0.7f, 60f * U), l.DisplayGoal, S(24, FontStyle.Normal, TextAnchor.UpperCenter, true), new Color(1, 1, 1, 0.92f * a), false);
            Text(new Rect(0, y + 196f * U, w, 36f * U), Loc.F("suggested mode  {0}   ·   par {1:0} s   ·   best {2}", l.recommendedMode.ToString().ToUpper(), l.parTime, Stars(l.BestStars)),
                S(19, FontStyle.Normal, TextAnchor.MiddleCenter), new Color(1, 1, 1, 0.7f * a), false);
        }
    }

    // ---- option panels (pause / results) --------------------------------------------------------
    void DrawOptionPanel(float w, float h, string title, string subtitle, string[] options, Color accent)
    {
        float pw = 520f * U, oh = 62f * U, gap = 12f * U;
        float ph = 170f * U + options.Length * (oh + gap) + 40f * U;
        var r = new Rect((w - pw) * 0.5f, (h - ph) * 0.5f, pw, ph);
        Glow(r.center, ph * 0.9f, new Color(accent.r, accent.g, accent.b, 0.18f));
        Rounded(r, new Color(0.05f, 0.06f, 0.09f, 0.94f), 22f * U);
        Rounded(new Rect(r.x, r.y, r.width, 8f * U), accent, 4f * U);
        Text(new Rect(r.x, r.y + 34f * U, r.width, 60f * U), title, S(46, FontStyle.Bold, TextAnchor.MiddleCenter), Color.white);
        Text(new Rect(r.x, r.y + 96f * U, r.width, 34f * U), subtitle, S(19, FontStyle.Normal, TextAnchor.MiddleCenter), accent, false);
        DrawOptions(new Rect(r.x + 40f * U, r.y + 160f * U, r.width - 80f * U, options.Length * (oh + gap)), options, accent, oh, gap);
        Footer(w, h, Loc.T("↑  ↓  choose      ENTER / ⓐ  select      ESC / ⓑ  back"));
    }

    void DrawOptions(Rect area, string[] options, Color accent, float oh, float gap)
    {
        optionRects.Clear();
        for (int i = 0; i < options.Length; i++)
        {
            var r = new Rect(area.x, area.y + i * (oh + gap), area.width, oh);
            optionRects.Add(r);
            bool sel = i == selOption;
            Rounded(r, sel ? new Color(accent.r, accent.g, accent.b, 0.25f) : new Color(1, 1, 1, 0.06f), 12f * U);
            if (sel) Rounded(r, new Color(accent.r, accent.g, accent.b, 0.9f), 12f * U, 2.5f * U);
            Text(r, Loc.T(options[i]), S(26, sel ? FontStyle.Bold : FontStyle.Normal, TextAnchor.MiddleCenter), sel ? Color.white : new Color(1, 1, 1, 0.75f), false);
        }
    }

    // ---- results card -------------------------------------------------------------------------------
    void DrawResults(float w, float h)
    {
        var look = LookFor(result.discipline);
        float t = stateTime;
        float pop = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.35f));
        var opts = ResultOptions();
        float pw = 760f * U, oh = 58f * U, gap = 10f * U;
        float ph = 470f * U + opts.Length * (oh + gap);
        var r = new Rect((w - pw) * 0.5f, (h - ph) * 0.5f - (1f - pop) * 40f * U, pw, ph);
        Color accent = result.stars == 3 ? new Color(1f, 0.85f, 0.3f) : look.accent;
        Glow(r.center, ph * 0.95f, new Color(accent.r, accent.g, accent.b, 0.25f * pop));
        Rounded(r, new Color(0.05f, 0.06f, 0.09f, 0.94f * pop), 24f * U);
        Rounded(new Rect(r.x, r.y, r.width, 8f * U), accent, 4f * U);

        string headline = Loc.T(result.stars == 3 ? "PERFECT RUN" : result.stars == 2 ? "DRILL COMPLETE" : result.stars == 1 ? "DRILL PASSED" : "RUN OVER");
        Text(new Rect(r.x, r.y + 30f * U, r.width, 60f * U), headline, S(48, FontStyle.Bold, TextAnchor.MiddleCenter), new Color(1, 1, 1, pop));
        Text(new Rect(r.x, r.y + 92f * U, r.width, 34f * U), Loc.T(result.discipline) + "  ·  " + Loc.T(result.title), S(21, FontStyle.Normal, TextAnchor.MiddleCenter), new Color(accent.r, accent.g, accent.b, pop), false);

        // stars
        float starSize = 120f * U;
        float sx = r.center.x - starSize * 1.5f;
        for (int i = 0; i < 3; i++)
        {
            float appear = Mathf.Clamp01((t - 0.5f - i * 0.45f) / 0.3f);
            bool earned = i < result.stars;
            float scale = earned ? 1.6f - 0.6f * Mathf.SmoothStep(0f, 1f, appear) : 1f;
            var sr = new Rect(sx + i * starSize, r.y + 130f * U, starSize, starSize);
            if (earned && appear > 0f)
            {
                Glow(sr.center, starSize * 0.9f * appear, new Color(1f, 0.85f, 0.3f, 0.35f * appear));
                var st = S(Mathf.RoundToInt(96f * scale), FontStyle.Normal, TextAnchor.MiddleCenter);
                Text(sr, "★", st, new Color(1f, 0.85f, 0.3f, appear));
            }
            else Text(sr, "☆", S(96, FontStyle.Normal, TextAnchor.MiddleCenter), new Color(1, 1, 1, 0.25f * pop), false);
        }

        // score counting up
        float countT = Mathf.Clamp01((t - 0.4f) / 1.2f);
        int shown = Mathf.RoundToInt(result.score * Mathf.SmoothStep(0f, 1f, countT));
        Text(new Rect(r.x, r.y + 262f * U, r.width, 80f * U), shown.ToString(), S(72, FontStyle.Bold, TextAnchor.MiddleCenter), new Color(1, 1, 1, pop));
        Text(new Rect(r.x, r.y + 336f * U, r.width, 30f * U), Loc.T("SCORE / 100"), S(15, FontStyle.Bold, TextAnchor.MiddleCenter), new Color(1, 1, 1, 0.5f * pop), false);

        string stats = Loc.F("time  <b>{0:0.0} s</b>      crashes  <b>{1}</b>      best  <b>{2}</b>", result.elapsed, result.crashes, Stars(result.bestStars));
        Text(new Rect(r.x, r.y + 372f * U, r.width, 34f * U), stats, S(20, FontStyle.Normal, TextAnchor.MiddleCenter), new Color(1, 1, 1, 0.8f * pop), false);
        if (result.newBest && t > 1.9f)
        {
            float bl = 0.7f + 0.3f * Mathf.Sin(t * 6f);
            Text(new Rect(r.x, r.y + 404f * U, r.width, 34f * U), Loc.T("NEW PERSONAL BEST"), S(22, FontStyle.Bold, TextAnchor.MiddleCenter), new Color(1f, 0.85f, 0.3f, bl));
        }
        if (t > 0.8f) DrawOptions(new Rect(r.x + 120f * U, r.y + 446f * U, r.width - 240f * U, opts.Length * (oh + gap)), opts, accent, oh, gap);
        Footer(w, h, Loc.T("↑  ↓  choose      ENTER / ⓐ  select      ESC  drill list"));
    }

    // ---- icons ------------------------------------------------------------------------------------------
    void DrawIcon(int icon, Rect r, Color c, bool lit)
    {
        Color dim = new Color(c.r, c.g, c.b, lit ? 1f : 0.7f);
        float u = r.width / 120f;
        void Tex(Rect rr, Texture2D tex) { GUI.color = dim; GUI.DrawTexture(rr, tex); GUI.color = Color.white; }
        switch (icon)
        {
            case 0: // hover box + quad
                Rounded(new Rect(r.x + 10 * u, r.y + 10 * u, 100 * u, 100 * u), dim, 12 * u, 5 * u);
                for (int i = 0; i < 4; i++)
                {
                    float ax = r.center.x + (i % 2 == 0 ? -22 : 22) * u, ay = r.center.y + (i < 2 ? -22 : 22) * u;
                    Tex(new Rect(ax - 16 * u, ay - 16 * u, 32 * u, 32 * u), ring);
                }
                Tex(new Rect(r.center.x - 10 * u, r.center.y - 10 * u, 20 * u, 20 * u), circle);
                break;
            case 1: // gate arch + flag
                Rounded(new Rect(r.x + 14 * u, r.y + 30 * u, 14 * u, 80 * u), dim, 4 * u);
                Rounded(new Rect(r.xMax - 28 * u, r.y + 30 * u, 14 * u, 80 * u), dim, 4 * u);
                Rounded(new Rect(r.x + 14 * u, r.y + 18 * u, r.width - 28 * u, 14 * u), dim, 4 * u);
                for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) if ((x + y) % 2 == 0)
                            Tex(new Rect(r.center.x - 27 * u + x * 18 * u, r.y + 50 * u + y * 18 * u, 18 * u, 18 * u), white);
                break;
            case 2: // loop with arrow head
                Tex(new Rect(r.x + 8 * u, r.y + 8 * u, 104 * u, 104 * u), ring);
                GUIUtility.RotateAroundPivot(45f, new Vector2(r.center.x + 42 * u, r.center.y - 30 * u));
                Rounded(new Rect(r.center.x + 30 * u, r.center.y - 36 * u, 26 * u, 12 * u), dim, 3 * u);
                GUI.matrix = Matrix4x4.identity;
                GUIUtility.RotateAroundPivot(-45f, new Vector2(r.center.x + 42 * u, r.center.y - 30 * u));
                Rounded(new Rect(r.center.x + 30 * u, r.center.y - 36 * u, 26 * u, 12 * u), dim, 3 * u);
                GUI.matrix = Matrix4x4.identity;
                break;
            case 3: // starburst
                for (int i = 0; i < 8; i++)
                {
                    GUIUtility.RotateAroundPivot(i * 45f, r.center);
                    Rounded(new Rect(r.center.x - 5 * u, r.center.y - (i % 2 == 0 ? 56 : 40) * u, 10 * u, (i % 2 == 0 ? 56 : 40) * u), dim, 4 * u);
                    GUI.matrix = Matrix4x4.identity;
                }
                Tex(new Rect(r.center.x - 16 * u, r.center.y - 16 * u, 32 * u, 32 * u), circle);
                break;
            case 4: // chip
                Rounded(new Rect(r.x + 26 * u, r.y + 26 * u, 68 * u, 68 * u), dim, 8 * u, 5 * u);
                Rounded(new Rect(r.x + 46 * u, r.y + 46 * u, 28 * u, 28 * u), dim, 4 * u);
                for (int i = 0; i < 4; i++)
                {
                    float p = r.x + 34 * u + i * 17 * u;
                    Rounded(new Rect(p, r.y + 10 * u, 6 * u, 14 * u), dim, 2 * u);
                    Rounded(new Rect(p, r.yMax - 24 * u, 6 * u, 14 * u), dim, 2 * u);
                    Rounded(new Rect(r.x + 10 * u, p - r.x + r.y, 14 * u, 6 * u), dim, 2 * u);
                    Rounded(new Rect(r.xMax - 24 * u, p - r.x + r.y, 14 * u, 6 * u), dim, 2 * u);
                }
                break;
            default: // cloud / free
                Tex(new Rect(r.x + 20 * u, r.y + 40 * u, 44 * u, 44 * u), circle);
                Tex(new Rect(r.x + 42 * u, r.y + 22 * u, 56 * u, 56 * u), circle);
                Tex(new Rect(r.x + 64 * u, r.y + 44 * u, 40 * u, 40 * u), circle);
                Rounded(new Rect(r.x + 30 * u, r.y + 62 * u, 66 * u, 22 * u), dim, 8 * u);
                break;
        }
        GUI.color = Color.white;
    }

    // ---- generated textures ----------------------------------------------------------------------------
    static Texture2D Radial(int size, float inner, float outer, float power)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f)) / (size * 0.5f);
                float a = 1f - Mathf.InverseLerp(inner, outer, d);
                px[y * size + x] = new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(a), power));
            }
        t.SetPixels(px); t.Apply();
        return t;
    }

    static Texture2D Ring(int size)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color[size * size];
        float aa = 1.5f / size;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f)) / (size * 0.5f);
                float a = Mathf.Clamp01((d - 0.72f) / aa) * Mathf.Clamp01((1f - d) / aa);
                px[y * size + x] = new Color(1, 1, 1, a);
            }
        t.SetPixels(px); t.Apply();
        return t;
    }

    static Texture2D Gradient(int size, bool opaqueAtTop)
    {
        var t = new Texture2D(1, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < size; y++)
        {
            float k = y / (float)(size - 1);
            t.SetPixel(0, y, new Color(1, 1, 1, Mathf.SmoothStep(0f, 1f, opaqueAtTop ? k : 1f - k)));
        }
        t.Apply();
        return t;
    }
}

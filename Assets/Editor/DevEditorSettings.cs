using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// When the editor is launched for automated testing (-droneRemote / -droneSelfTest),
/// turn off the editor's background throttling so the sim keeps running at full
/// rate while another window has focus.
/// </summary>
[InitializeOnLoad]
public static class DevEditorSettings
{
    static DevEditorSettings()
    {
        var args = Environment.GetCommandLineArgs();
        if (Array.IndexOf(args, "-droneRemote") < 0 && Array.IndexOf(args, "-droneSelfTest") < 0) return;

        // 0 = Default (throttled), 1 = No throttling, 2 = Monitor refresh rate, 3 = Custom
        EditorPrefs.SetInt("InteractionMode", 1);
        // 0 = Recompile And Continue Playing, so the 'refresh' remote command hot-reloads scripts
        EditorPrefs.SetInt("ScriptCompilationDuringPlay", 0);
        Debug.Log("[DevEditorSettings] editor interaction throttling disabled for automated testing");
    }
}

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Feeds a captured transmitter stream through the very parsers <see cref="SerialRc"/> uses at
/// runtime, so the wire format can be proven end to end without the hardware: RemoteSim writes
/// frames to a file, this reads them back and checks the channels survive the round trip.
///
///   Unity.exe -batchmode -executeMethod SerialRcSelfTest.Run -rcCrsf crsf.bin -rcText rc.txt -quit
/// </summary>
public static class SerialRcSelfTest
{
    [MenuItem("Drone Trainer/Test RC link parsers")]
    public static void Run()
    {
        string crsfPath = Arg("-rcCrsf");
        string textPath = Arg("-rcText");
        int failures = 0;

        failures += RoundTrip();
        if (!string.IsNullOrEmpty(crsfPath)) failures += ReplayCrsf(crsfPath);
        if (!string.IsNullOrEmpty(textPath)) failures += ReplayText(textPath);

        Debug.Log(failures == 0 ? "[RcTest] PASS - all parser checks agreed" : $"[RcTest] FAIL - {failures} check(s) failed");
        if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    static string Six(int[] v) => $"{v[0]},{v[1]},{v[2]},{v[3]},{v[4]},{v[5]}";

    static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>Builds a CRSF frame the way the transmitter does and decodes it back.</summary>
    static int RoundTrip()
    {
        int[] sent = { 1000, 1250, 1500, 1750, 2000, 1500, 1500, 1500, 1500, 1500, 1500, 1500, 1500, 1500, 1500, 1500 };
        var frame = BuildCrsf(sent);
        var got = new int[SerialRc.Channels];
        if (!SerialRc.ParseCrsf(frame, frame.Length, got))
        {
            Debug.LogError("[RcTest] CRSF round trip: frame rejected by the parser");
            return 1;
        }
        int bad = 0;
        for (int i = 0; i < 6; i++)
        {
            // 11-bit channels quantise to ~0.625 us, so allow a microsecond of slack
            if (Mathf.Abs(got[i] - sent[i]) > 2) { Debug.LogError($"[RcTest] ch{i}: sent {sent[i]} us, decoded {got[i]} us"); bad++; }
        }
        Debug.Log($"[RcTest] CRSF round trip: {Six(got)} (sent {Six(sent)})");
        return bad;
    }

    /// <summary>Replays a captured CRSF byte stream through the same framing the reader thread uses.</summary>
    static int ReplayCrsf(string path)
    {
        if (!File.Exists(path)) { Debug.LogError($"[RcTest] capture not found: {path}"); return 1; }
        var bytes = File.ReadAllBytes(path);
        var frame = new byte[64];
        var got = new int[SerialRc.Channels];
        int frameLen = 0, expected = 0, good = 0, rejected = 0;
        int minRoll = int.MaxValue, maxRoll = int.MinValue;

        foreach (byte b in bytes)
        {
            if (frameLen == 0 && b == 0xC8) { frame[frameLen++] = b; continue; }
            if (frameLen == 0) continue;
            frame[frameLen++] = b;
            if (frameLen == 2) expected = Mathf.Clamp(b, 2, 62) + 2;
            if (frameLen >= expected)
            {
                if (SerialRc.ParseCrsf(frame, frameLen, got))
                {
                    good++;
                    minRoll = Mathf.Min(minRoll, got[0]);
                    maxRoll = Mathf.Max(maxRoll, got[0]);
                }
                else rejected++;
                frameLen = 0;
            }
        }

        bool ok = good > 0 && rejected == 0 && maxRoll - minRoll > 100;   // the capture flies a circle
        Debug.Log($"[RcTest] CRSF capture {Path.GetFileName(path)}: {good} frames decoded, {rejected} rejected, roll {minRoll}..{maxRoll} us");
        if (!ok) Debug.LogError("[RcTest] CRSF capture did not decode as expected");
        return ok ? 0 : 1;
    }

    static int ReplayText(string path)
    {
        if (!File.Exists(path)) { Debug.LogError($"[RcTest] capture not found: {path}"); return 1; }
        var got = new int[SerialRc.Channels];
        int good = 0, bad = 0;
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.Length == 0) continue;
            if (SerialRc.ParseText(line, got)) good++; else bad++;
        }
        bool ok = good > 0 && bad == 0;
        Debug.Log($"[RcTest] TEXT capture {Path.GetFileName(path)}: {good} lines decoded, {bad} rejected, last = {Six(got)}");
        if (!ok) Debug.LogError("[RcTest] TEXT capture did not decode as expected");
        return ok ? 0 : 1;
    }

    /// <summary>The transmitter side of the format, mirrored here so the test is self-contained.</summary>
    static byte[] BuildCrsf(int[] us)
    {
        var buf = new byte[26];
        buf[0] = 0xC8;
        buf[1] = 24;
        buf[2] = 0x16;
        int bitPos = 0;
        for (int ch = 0; ch < SerialRc.Channels; ch++)
        {
            int raw = Mathf.Clamp(Mathf.RoundToInt((us[ch] - 1500) * 8f / 5f) + 992, 0, 0x7FF);
            for (int bit = 0; bit < 11; bit++)
                if ((raw & (1 << bit)) != 0)
                    buf[3 + ((bitPos + bit) >> 3)] |= (byte)(1 << ((bitPos + bit) & 7));
            bitPos += 11;
        }
        buf[25] = SerialRc.Crc8(buf, 2, 23);
        return buf;
    }
}

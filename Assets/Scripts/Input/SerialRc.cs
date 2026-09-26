using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO.Ports;
using System.Threading;
using UnityEngine;

/// <summary>
/// Reads a real transmitter over a UART (serial port) and exposes its channels.
///
/// Two wire formats are understood:
///   CRSF  - what an ELRS / TBS receiver puts out: 0xC8, len, type, payload, CRC8 (poly 0xD5).
///           Type 0x16 carries 16 channels packed as 11 bits each, 172..1811 -> 988..2012 us.
///           Plain 8N1, usually 420000 baud.
///   TEXT  - one line per frame for hobby / Arduino remotes:  "$RC,1500,1500,1000,1500,...\n"
///           values in microseconds, any number of channels, newline terminated.
///
/// The port is serviced on its own thread: a dropped or slow link must never stall the flight
/// loop. Unity only ever reads the decoded channel array and the timestamp of the last good
/// frame, both guarded by a lock. If frames stop arriving the link is failsafe - the sim drops
/// the sticks and disarms, exactly like a real quad losing its transmitter.
/// </summary>
public class SerialRc : MonoBehaviour
{
    public enum Protocol { Auto, Crsf, Text }

    [Header("Port")]
    [Tooltip("Find the remote on any serial port and connect by itself")]
    public bool autoConnect = true;
    public string portName = "COM3";
    public int baudRate = 420000;
    public Protocol protocol = Protocol.Auto;
    [Tooltip("No frame for this long = link lost (a real receiver failsafes at ~1 s)")]
    public float failsafeSeconds = 0.5f;

    [Header("Channel map (0-based)")]
    public int chRoll = 0, chPitch = 1, chThrottle = 2, chYaw = 3, chArm = 4, chMode = 5;
    public bool revRoll, revPitch, revThrottle, revYaw;
    [Tooltip("Microseconds at full low / centre / full high")]
    public int usMin = 1000, usMid = 1500, usMax = 2000;

    public const int Channels = 16;

    // ---- state shared with the reader thread -------------------------------------------
    readonly object gate = new object();
    readonly int[] shared = new int[Channels];
    double lastFrameAt = -1000;
    int frameCount;
    long bytesReceived;
    string threadError;

    // ---- state owned by Unity ------------------------------------------------------------
    readonly int[] channels = new int[Channels];
    SerialPort port;
    Thread reader;
    volatile bool running;
    double openedAt;
    int lastFrameCount;
    float rateTimer;
    long lastBytes;

    public bool IsOpen => port != null && port.IsOpen;
    /// <summary>True when the port is open and frames are arriving.</summary>
    public bool HasSignal { get; private set; }
    public float FramesPerSecond { get; private set; }
    public string Status { get; private set; } = "not connected";
    /// <summary>Raw bytes per second off the wire, decoded or not. Bytes arriving with no frames
    /// decoded means the port is right but the protocol or the baud rate is not.</summary>
    public float BytesPerSecond { get; private set; }
    public string LastError { get; private set; }
    /// <summary>Raw channel values in microseconds (1000..2000), index 0..15.</summary>
    public int[] Channels_us => channels;
    public float SecondsSinceFrame { get; private set; } = 999f;

    // decoded controls, -1..1 (throttle 0..1)
    public float Roll { get; private set; }
    public float Pitch { get; private set; }
    public float Yaw { get; private set; }
    public float Throttle { get; private set; }
    public bool ArmSwitch { get; private set; }
    public bool ArmSwitchPressed { get; private set; }
    public bool ArmSwitchReleased { get; private set; }
    public FlightMode ModeSwitch { get; private set; } = FlightMode.Angle;
    public bool ModeSwitchChanged { get; private set; }

    /// <summary>True once frames have actually arrived, so a dropout can be told apart from a
    /// transmitter that was never plugged in. Cleared by <see cref="Close"/>.</summary>
    public bool EverHadSignal { get; private set; }

    bool lastArm;
    bool armNeedsCycle;
    FlightMode lastMode = FlightMode.Angle;

    void Start()
    {
        LoadSettings();
        for (int i = 0; i < Channels; i++) channels[i] = i == chThrottle ? usMin : usMid;
        knownPorts = AvailablePorts();
        if (autoConnect) scan = StartCoroutine(FindRemote());
    }

    void OnDestroy() => Close();
    void OnApplicationQuit() => Close();

    // =====================================================================================
    // Remote finder
    // =====================================================================================
    // Plug the remote in, start the game, fly. The COM number changes with the USB socket and the
    // wire format and baud rate are easy to get wrong, so with auto-connect on, the finder tries
    // the saved port first and then every other one, at the saved baud and the two usual ones,
    // decoding both formats, and keeps the first port that actually carries frames.
    //
    // Opening a port can reset whatever board sits behind it, so a port that opened and stayed
    // silent is not touched again until the set of ports changes (something was plugged in or
    // out). Only ports that were busy - another program had them - are retried on a timer.

    Coroutine scan;
    string[] knownPorts = new string[0];
    readonly HashSet<string> silentPorts = new HashSet<string>();
    readonly HashSet<string> busyPorts = new HashSet<string>();
    float nextPortCheck;

    public bool Scanning => scan != null;

    IEnumerator FindRemote()
    {
        string savedPort = portName;
        int savedBaud = baudRate;
        protocol = Protocol.Auto;
        var ports = AvailablePorts().OrderBy(p => p == savedPort ? 0 : 1).ToArray();
        var bauds = new[] { savedBaud, 115200, 420000 }.Distinct().ToArray();

        foreach (var candidate in ports)
        {
            if (silentPorts.Contains(candidate)) continue;
            bool busy = false;
            for (int b = 0; b < bauds.Length; b++)
            {
                portName = candidate;
                baudRate = bauds[b];
                Status = $"looking for the remote on {candidate}";
                Open();
                if (!IsOpen) { busy = true; break; }
                // The first try on a port waits long enough for a board that reboots when its
                // port is opened (DTR/RTS wired to reset) to come back up and start talking.
                float until = Time.unscaledTime + (b == 0 ? 0.9f : 0.4f);
                while (Time.unscaledTime < until && !HasSignal) yield return null;
                if (HasSignal)
                {
                    busyPorts.Remove(candidate);
                    SaveSettings();
                    scan = null;
                    yield break;
                }
                Close(false);
            }
            if (busy) busyPorts.Add(candidate);
            else { busyPorts.Remove(candidate); silentPorts.Add(candidate); }
        }

        portName = savedPort;
        baudRate = savedBaud;
        Status = "remote not found";
        scan = null;
    }

    /// <summary>With no remote connected, rescan when a port appears or a busy one may have freed up.</summary>
    void WatchForRemote()
    {
        if (!autoConnect || scan != null || IsOpen || Time.unscaledTime < nextPortCheck) return;
        nextPortCheck = Time.unscaledTime + 3f;
        var ports = AvailablePorts();
        bool changed = ports.Length != knownPorts.Length || ports.Except(knownPorts).Any();
        if (changed) silentPorts.Clear();
        knownPorts = ports;
        // A link that was working and died (cable pulled) is worth chasing until it comes back.
        if (changed || busyPorts.Count > 0 || EverHadSignal) scan = StartCoroutine(FindRemote());
    }

    public static string[] AvailablePorts()
    {
        try { return SerialPort.GetPortNames(); }
        catch { return new string[0]; }
    }

    public void Open()
    {
        Close(false);            // switching ports is not the pilot disconnecting: keep the dropout state
        try
        {
            port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 50,
                WriteTimeout = 50,
                DtrEnable = true,
                RtsEnable = true,
            };
            port.Open();
            running = true;
            openedAt = NowSeconds();
            Interlocked.Exchange(ref bytesReceived, 0);
            lastBytes = 0;
            threadError = null;
            reader = new Thread(ReadLoop) { IsBackground = true, Name = "SerialRc" };
            reader.Start();
            Status = $"{portName} open at {baudRate}";
            LastError = null;
        }
        catch (Exception e)
        {
            LastError = e.Message;
            Status = $"{portName}: {e.Message}";
            port = null;
        }
    }

    /// <summary>Close the port. <paramref name="deliberate"/> is false when the link died on its own
    /// (cable pulled, board reset): that is a dropout the failsafe must still see.</summary>
    public void Close(bool deliberate = true)
    {
        running = false;
        if (reader != null) { reader.Join(200); reader = null; }
        if (port != null)
        {
            try { if (port.IsOpen) port.Close(); } catch { }
            port.Dispose();
            port = null;
        }
        HasSignal = false;
        if (deliberate) EverHadSignal = false;      // deliberately disconnected is not a dropout
        Status = "not connected";
    }

    /// <summary>Look for the remote now, on every port - used when auto-connect is switched on.</summary>
    public void StartScan()
    {
        if (scan != null || IsOpen) return;
        silentPorts.Clear();
        scan = StartCoroutine(FindRemote());
    }

    /// <summary>The pilot picked a port by hand: stop the finder from overriding the choice.</summary>
    public void StopScan()
    {
        if (scan != null) StopCoroutine(scan);
        scan = null;
    }

    static double NowSeconds() => DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;

    // =====================================================================================
    // Reader thread
    // =====================================================================================

    // Decoder state: owned by whoever is feeding bytes (the reader thread, or InjectBytes).
    readonly byte[] frame = new byte[64];
    int frameLen, expected;
    readonly System.Text.StringBuilder text = new System.Text.StringBuilder(128);
    readonly int[] decoded = new int[Channels];

    /// <summary>
    /// Push received bytes through the decoder. Used by the reader thread, and by DevRemote to
    /// replay a captured transmitter stream so the whole path can be tested without hardware.
    /// </summary>
    public void InjectBytes(byte[] data, int count)
    {
        for (int i = 0; i < count; i++) FeedByte(data[i]);
    }

    void FeedByte(byte b)
    {
        bool wantCrsf = protocol == Protocol.Crsf || protocol == Protocol.Auto;
        bool wantText = protocol == Protocol.Text || protocol == Protocol.Auto;

        // ---- CRSF: 0xC8, length, type, payload, crc ----
        if (wantCrsf && frameLen == 0 && b == 0xC8) { frame[frameLen++] = b; expected = 0; return; }
        if (frameLen > 0)
        {
            frame[frameLen++] = b;
            if (frameLen == 2) expected = Mathf.Clamp(b, 2, 62) + 2;   // sync + len byte + payload
            if (frameLen >= expected)
            {
                if (ParseCrsf(frame, frameLen, decoded)) Publish(decoded);
                frameLen = 0;
            }
            return;
        }

        // ---- TEXT: "$RC,1500,1500,..." terminated by newline ----
        if (!wantText) return;
        if (b == (byte)'\n' || b == (byte)'\r')
        {
            if (text.Length > 0)
            {
                if (ParseText(text.ToString(), decoded)) Publish(decoded);
                text.Clear();
            }
        }
        else if (text.Length < 120) text.Append((char)b);
    }

    /// <summary>Serial reader thread: nothing here may touch the Unity API or block the flight loop.</summary>
    void ReadLoop()
    {
        var buffer = new byte[512];
        while (running)
        {
            int read;
            try { read = port.Read(buffer, 0, buffer.Length); }
            catch (TimeoutException) { continue; }
            catch (Exception e) { threadError = e.Message; break; }
            Interlocked.Add(ref bytesReceived, read);
            for (int i = 0; i < read; i++) FeedByte(buffer[i]);
        }
    }

    void Publish(int[] decoded)
    {
        lock (gate)
        {
            Array.Copy(decoded, shared, Channels);
            lastFrameAt = NowSeconds();
            frameCount++;
        }
    }

    /// <summary>CRC8 with polynomial 0xD5 over type + payload, as CRSF specifies.</summary>
    public static byte Crc8(byte[] data, int offset, int length)
    {
        byte crc = 0;
        for (int i = 0; i < length; i++)
        {
            crc ^= data[offset + i];
            for (int b = 0; b < 8; b++) crc = (byte)((crc & 0x80) != 0 ? (crc << 1) ^ 0xD5 : crc << 1);
        }
        return crc;
    }

    /// <summary>Decodes a complete CRSF frame; true only for a valid RC-channels frame.</summary>
    public static bool ParseCrsf(byte[] f, int len, int[] outUs)
    {
        if (len < 4 || f[0] != 0xC8) return false;
        int lenByte = f[1];
        if (lenByte + 2 != len) return false;
        byte type = f[2];
        byte crc = f[len - 1];
        if (Crc8(f, 2, lenByte - 1) != crc) return false;
        if (type != 0x16 || lenByte < 24) return false;          // RC channels packed

        // 16 channels x 11 bits, little endian, starting at payload byte 0 (frame index 3)
        int bitPos = 0;
        for (int ch = 0; ch < Channels; ch++)
        {
            int byteIndex = 3 + (bitPos >> 3);
            int shift = bitPos & 7;
            int raw = (f[byteIndex] | (f[byteIndex + 1] << 8) | (f[byteIndex + 2] << 16)) >> shift;
            raw &= 0x7FF;
            bitPos += 11;
            // CRSF 172..1811 maps to 988..2012 us
            outUs[ch] = Mathf.RoundToInt((raw - 992) * 5f / 8f + 1500f);
        }
        return true;
    }

    /// <summary>Decodes "$RC,1500,1500,..." (microseconds). Leading token is ignored.</summary>
    public static bool ParseText(string line, int[] outUs)
    {
        int start = line.IndexOf(',');
        if (start < 0) return false;
        if (!line.StartsWith("$RC") && !line.StartsWith("RC")) return false;
        int ch = 0;
        int i = start + 1;
        while (i <= line.Length && ch < Channels)
        {
            int comma = line.IndexOf(',', i);
            if (comma < 0) comma = line.Length;
            if (comma > i && int.TryParse(line.Substring(i, comma - i).Trim(), out int us))
                outUs[ch++] = Mathf.Clamp(us, 800, 2200);
            else if (comma > i) return false;
            i = comma + 1;
            if (comma >= line.Length) break;
        }
        return ch >= 4;
    }

    // =====================================================================================
    // Unity side
    // =====================================================================================

    void Update()
    {
        ArmSwitchPressed = ArmSwitchReleased = ModeSwitchChanged = false;

        int frames;
        double stamp;
        lock (gate)
        {
            Array.Copy(shared, channels, Channels);
            stamp = lastFrameAt;
            frames = frameCount;
        }

        if (threadError != null)
        {
            // The link died by itself (cable pulled, board reset): a dropout the failsafe must see,
            // not a disconnect. Report it and let the finder watch for the port coming back.
            LastError = threadError;
            threadError = null;
            Close(false);
            Status = $"{portName}: {LastError}";
        }
        WatchForRemote();

        SecondsSinceFrame = (float)(NowSeconds() - stamp);
        // Signal means frames are arriving, not merely that a port is open: a port can be open
        // with a dead transmitter on the other end, and frames can be injected for testing.
        HasSignal = SecondsSinceFrame < failsafeSeconds;
        if (HasSignal) EverHadSignal = true;

        rateTimer += Time.unscaledDeltaTime;
        if (rateTimer >= 0.5f)
        {
            FramesPerSecond = (frames - lastFrameCount) / rateTimer;
            lastFrameCount = frames;
            long bytes = Interlocked.Read(ref bytesReceived);
            BytesPerSecond = (bytes - lastBytes) / rateTimer;
            lastBytes = bytes;
            rateTimer = 0f;
        }
        if (IsOpen) Status = HasSignal ? $"{portName}  {FramesPerSecond:0} Hz"
                           : BytesPerSecond > 20f ? $"{portName}  data arriving, but not as {protocol} frames"
                           : $"{portName}  NO SIGNAL";
        else if (HasSignal) Status = $"replay  {FramesPerSecond:0} Hz";

        if (!HasSignal)
        {
            // Failsafe stage one: sticks to neutral, throttle closed. Cutting the motors is left
            // to RcLink, which owns the failsafe procedure and knows whether the quad is airborne.
            Roll = Pitch = Yaw = 0f;
            Throttle = 0f;
            ArmSwitch = false;
            lastArm = false;
            armNeedsCycle = true;
            return;
        }

        Roll = Axis(chRoll, revRoll);
        Pitch = Axis(chPitch, revPitch);
        Yaw = Axis(chYaw, revYaw);
        float t = Axis(chThrottle, revThrottle);
        Throttle = Mathf.Clamp01((t + 1f) * 0.5f);

        ArmSwitch = Us(chArm) > (usMid + usMax) * 0.5f;
        // Betaflight will not arm with the switch already on: after a dropout the pilot has to
        // flip it off and back on, so a link that comes back can never spin the motors by itself.
        if (armNeedsCycle && !ArmSwitch) armNeedsCycle = false;
        if (ArmSwitch && !lastArm && !armNeedsCycle) ArmSwitchPressed = true;
        if (!ArmSwitch && lastArm) ArmSwitchReleased = true;
        lastArm = ArmSwitch;

        int modeUs = Us(chMode);
        ModeSwitch = modeUs < (usMin + usMid) * 0.5f ? FlightMode.Angle
                   : modeUs < (usMid + usMax) * 0.5f ? FlightMode.Horizon
                   : FlightMode.Acro;
        if (ModeSwitch != lastMode) { ModeSwitchChanged = true; lastMode = ModeSwitch; }
    }

    int Us(int ch) => ch >= 0 && ch < Channels ? channels[ch] : usMid;

    /// <summary>Channel as -1..1 using the configured endpoints.</summary>
    float Axis(int ch, bool reverse)
    {
        int us = Us(ch);
        float v = us >= usMid
            ? Mathf.InverseLerp(usMid, usMax, us)
            : -Mathf.InverseLerp(usMid, usMin, us);
        return Mathf.Clamp(reverse ? -v : v, -1f, 1f);
    }

    // ---- persistence ---------------------------------------------------------------------

    public void SaveSettings()
    {
        PlayerPrefs.SetString("rc.port", portName);
        PlayerPrefs.SetInt("rc.baud", baudRate);
        PlayerPrefs.SetInt("rc.protocol", (int)protocol);
        PlayerPrefs.SetInt("rc.auto", autoConnect ? 1 : 0);
        PlayerPrefs.SetString("rc.map", $"{chRoll},{chPitch},{chThrottle},{chYaw},{chArm},{chMode}");
        PlayerPrefs.SetString("rc.rev", $"{(revRoll ? 1 : 0)},{(revPitch ? 1 : 0)},{(revThrottle ? 1 : 0)},{(revYaw ? 1 : 0)}");
        PlayerPrefs.SetString("rc.ends", $"{usMin},{usMid},{usMax}");
        PlayerPrefs.Save();
    }

    void LoadSettings()
    {
        portName = PlayerPrefs.GetString("rc.port", portName);
        baudRate = PlayerPrefs.GetInt("rc.baud", baudRate);
        protocol = (Protocol)PlayerPrefs.GetInt("rc.protocol", (int)protocol);
        autoConnect = PlayerPrefs.GetInt("rc.auto", autoConnect ? 1 : 0) == 1;
        var map = PlayerPrefs.GetString("rc.map", "").Split(',');
        if (map.Length == 6)
        {
            int.TryParse(map[0], out chRoll); int.TryParse(map[1], out chPitch);
            int.TryParse(map[2], out chThrottle); int.TryParse(map[3], out chYaw);
            int.TryParse(map[4], out chArm); int.TryParse(map[5], out chMode);
        }
        var rev = PlayerPrefs.GetString("rc.rev", "").Split(',');
        if (rev.Length == 4)
        {
            revRoll = rev[0] == "1"; revPitch = rev[1] == "1";
            revThrottle = rev[2] == "1"; revYaw = rev[3] == "1";
        }
        var ends = PlayerPrefs.GetString("rc.ends", "").Split(',');
        if (ends.Length == 3)
        {
            int.TryParse(ends[0], out usMin); int.TryParse(ends[1], out usMid); int.TryParse(ends[2], out usMax);
        }

        // Settings saved before the remote finder existed: auto-connect was off by default and the
        // protocol stayed wherever it was last clicked - which is how a Text remote ended up being
        // silently ignored as Crsf. Move them onto the finder once; later choices are respected.
        if (PlayerPrefs.GetInt("rc.v", 1) < 2)
        {
            protocol = Protocol.Auto;
            autoConnect = true;
            PlayerPrefs.SetInt("rc.v", 2);
            SaveSettings();
        }
    }
}

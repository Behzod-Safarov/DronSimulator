using System.IO.Ports;

namespace RemoteSim;

/// <summary>
/// Transmitter simulator: pretends to be an RC radio and streams control data down a UART,
/// so the simulator's serial input can be developed and flown without the real hardware.
///
/// It speaks the same two formats DroneTrainer's RcLink reads:
///   crsf  - 0xC8, len, 0x16, 16 channels x 11 bits, CRC8 (poly 0xD5).  Real ELRS/TBS format.
///   text  - "$RC,1500,1500,1000,1500,1000,1000\n", microseconds, easy to eyeball in a terminal.
///
/// Sticks come from the keyboard (this is a simulator, not a radio), or from a scripted
/// pattern for hands-off testing, or held at centre.
///
///   RemoteSim --port COM12                    keyboard sticks, CRSF at 50 Hz
///   RemoteSim --port COM12 --format text      same, human-readable frames
///   RemoteSim --port COM12 --pattern circle   flies a repeating pattern, no keyboard
///   RemoteSim --file frames.bin --pattern circle --seconds 2     capture frames to a file
/// </summary>
internal static class Program
{
    const int ChannelCount = 16;

    static int Main(string[] args)
    {
        string port = null, file = null, format = "crsf", pattern = "keys";
        int baud = 420000, rateHz = 50;
        double seconds = 0;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : null;
            switch (a)
            {
                case "--port": port = Next(); break;
                case "--file": file = Next(); break;
                case "--baud": baud = int.Parse(Next()!); break;
                case "--format": format = Next()!.ToLowerInvariant(); break;
                case "--pattern": pattern = Next()!.ToLowerInvariant(); break;
                case "--rate": rateHz = int.Parse(Next()!); break;
                case "--seconds": seconds = double.Parse(Next()!); break;
                case "--list":
                    foreach (var p in SerialPort.GetPortNames()) Console.WriteLine(p);
                    return 0;
                case "--help":
                case "-h":
                    Console.WriteLine(Help);
                    return 0;
            }
        }

        if (port == null && file == null)
        {
            Console.WriteLine(Help);
            Console.WriteLine("\nPorts on this machine:");
            foreach (var p in SerialPort.GetPortNames()) Console.WriteLine("  " + p);
            return 1;
        }

        SerialPort sp = null;
        FileStream fs = null;
        try
        {
            if (port != null)
            {
                sp = new SerialPort(port, baud, Parity.None, 8, StopBits.One) { WriteTimeout = 200 };
                sp.Open();
                Console.WriteLine($"Transmitting {format.ToUpperInvariant()} on {port} at {baud} baud, {rateHz} Hz.");
            }
            if (file != null)
            {
                fs = File.Create(file);
                Console.WriteLine($"Writing {format.ToUpperInvariant()} frames to {file}.");
            }
        }
        catch (Exception e)
        {
            // "Could not find file 'COM4'" means the port is not there at all, which is easy to
            // hit because Windows remembers the names of adapters that are currently unplugged.
            Console.Error.WriteLine("Could not open output: " + e.Message);
            var present = SerialPort.GetPortNames();
            Console.Error.WriteLine(present.Length > 0
                ? "Ports present right now: " + string.Join(", ", present)
                : "No serial ports are present right now - plug the USB-serial adapter in first.");
            return 2;
        }

        var sticks = new Sticks();
        bool keys = pattern == "keys" && !Console.IsInputRedirected;
        if (keys) Console.WriteLine(KeyHelp);

        var channels = new int[ChannelCount];
        for (int i = 0; i < ChannelCount; i++) channels[i] = 1500;
        channels[2] = 1000;                     // throttle closed
        channels[4] = 1000;                     // disarmed
        channels[5] = 1000;                     // Angle mode

        var started = DateTime.UtcNow;
        var next = DateTime.UtcNow;
        int frames = 0;
        var buffer = new byte[64];

        while (true)
        {
            double t = (DateTime.UtcNow - started).TotalSeconds;
            if (seconds > 0 && t >= seconds) break;

            if (keys)
            {
                while (Console.KeyAvailable)
                {
                    var k = Console.ReadKey(true);
                    if (k.Key == ConsoleKey.Q) goto done;
                    sticks.Apply(k.Key);
                }
                sticks.Decay();
                sticks.Write(channels);
            }
            else if (pattern != "centre" && pattern != "center")
            {
                Pattern(pattern, t, channels);
            }

            int len = format == "text" ? BuildText(channels, buffer) : BuildCrsf(channels, buffer);
            try
            {
                sp?.Write(buffer, 0, len);
                fs?.Write(buffer, 0, len);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Write failed: " + e.Message);
                break;
            }
            frames++;

            if (keys && frames % rateHz == 0)
                Console.Write($"\rA{channels[0]}  E{channels[1]}  T{channels[2]}  R{channels[3]}  " +
                              $"arm={(channels[4] > 1700 ? "ON " : "off")}  mode={ModeName(channels[5])}   {frames} frames ");

            next = next.AddSeconds(1.0 / rateHz);
            var wait = next - DateTime.UtcNow;
            if (wait.TotalMilliseconds > 0) Thread.Sleep(wait);
            else next = DateTime.UtcNow;
        }

    done:
        sp?.Close();
        fs?.Dispose();
        Console.WriteLine($"\nSent {frames} frames.");
        return 0;
    }

    static string ModeName(int us) => us < 1250 ? "ANGLE" : us < 1750 ? "HORIZON" : "ACRO";

    /// <summary>Hands-off test patterns, so the link can be exercised without a keyboard.</summary>
    static void Pattern(string name, double t, int[] ch)
    {
        switch (name)
        {
            case "circle":                       // gentle circle at half throttle, armed
                ch[0] = 1500 + (int)(350 * Math.Sin(t * 1.2));
                ch[1] = 1500 + (int)(350 * Math.Cos(t * 1.2));
                ch[2] = t > 2.5 ? 1500 : 1000;   // throttle stays down until the quad is armed
                ch[3] = 1500;
                ch[4] = t > 2 ? 2000 : 1000;     // arm after two seconds
                ch[5] = 1000;
                break;
            case "checkout":                     // full systems check: arm, every mode, every stick, disarm
            {
                double c = t % 12.0;             // one twelve-second cycle, then it repeats
                ch[0] = 1500 + (int)(300 * Math.Sin(c));
                ch[1] = 1500 + (int)(300 * Math.Cos(c));
                ch[2] = c > 1.5 && c < 11 ? 1550 : 1000;      // throttle down while arming, like a real pilot
                ch[3] = 1500 + (int)(200 * Math.Sin(c * 0.5));
                ch[4] = c > 1 && c < 11 ? 2000 : 1000;        // armed for the middle of the cycle
                ch[5] = c < 5 ? 1000 : c < 8 ? 1500 : 2000;   // Angle -> Horizon -> Acro
                break;
            }
            case "sweep":                        // every stick to both extremes in turn
                int phase = (int)(t % 8);
                for (int i = 0; i < 4; i++) ch[i] = 1500;
                ch[2] = 1000;
                double s = Math.Sin(t * Math.PI);
                if (phase < 2) ch[0] = 1500 + (int)(500 * s);
                else if (phase < 4) ch[1] = 1500 + (int)(500 * s);
                else if (phase < 6) ch[2] = 1500 + (int)(500 * s);
                else ch[3] = 1500 + (int)(500 * s);
                ch[4] = 1000;
                break;
            default:                             // hold centre
                break;
        }
    }

    // ---------------------------------------------------------------- frame builders

    /// <summary>CRSF RC-channels frame: 0xC8, len, 0x16, 22 bytes of 11-bit channels, CRC8.</summary>
    public static int BuildCrsf(int[] us, byte[] outBuf)
    {
        Span<ushort> raw = stackalloc ushort[ChannelCount];
        for (int i = 0; i < ChannelCount; i++)
        {
            // inverse of the reader: 988..2012 us -> 172..1811
            int v = (int)Math.Round((us[i] - 1500) * 8.0 / 5.0) + 992;
            raw[i] = (ushort)Math.Clamp(v, 0, 0x7FF);
        }

        outBuf[0] = 0xC8;
        outBuf[1] = 24;          // type + 22 payload + crc
        outBuf[2] = 0x16;

        for (int i = 3; i < 3 + 22; i++) outBuf[i] = 0;
        int bitPos = 0;
        for (int chIdx = 0; chIdx < ChannelCount; chIdx++)
        {
            int value = raw[chIdx];
            for (int bit = 0; bit < 11; bit++)
            {
                if ((value & (1 << bit)) != 0)
                {
                    int index = 3 + ((bitPos + bit) >> 3);
                    outBuf[index] |= (byte)(1 << ((bitPos + bit) & 7));
                }
            }
            bitPos += 11;
        }

        outBuf[25] = Crc8(outBuf, 2, 23);   // over type + payload
        return 26;
    }

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

    public static int BuildText(int[] us, byte[] outBuf)
    {
        string line = "$RC," + string.Join(',', us.Take(8)) + "\n";
        return System.Text.Encoding.ASCII.GetBytes(line, outBuf);
    }

    const string Help = """
        RemoteSim - transmitter simulator for DroneTrainer, streams RC channels over a UART.

          --port COM12        serial port to transmit on
          --baud 420000       baud rate (CRSF uses 420000; text is happy at 115200)
          --format crsf|text  wire format (default crsf)
          --pattern keys|circle|checkout|sweep|centre   stick source (default keys)
          --rate 50           frames per second
          --file frames.bin   also (or instead) write the frames to a file
          --seconds 5         stop after this long
          --list              list serial ports
        """;

    const string KeyHelp = """
        Sticks:  W/S throttle   A/D yaw   arrows pitch/roll   SPACE arm switch   M mode switch
        Q quits.  Sticks self-centre except throttle, like a real radio.
        """;

    /// <summary>Keyboard stick model: self-centring except throttle, like a real transmitter.</summary>
    sealed class Sticks
    {
        public float Roll, Pitch, Yaw, Throttle;
        public bool Arm;
        public int Mode;

        public void Apply(ConsoleKey k)
        {
            const float step = 0.22f;
            switch (k)
            {
                case ConsoleKey.LeftArrow: Roll -= step; break;
                case ConsoleKey.RightArrow: Roll += step; break;
                case ConsoleKey.UpArrow: Pitch += step; break;
                case ConsoleKey.DownArrow: Pitch -= step; break;
                case ConsoleKey.A: Yaw -= step; break;
                case ConsoleKey.D: Yaw += step; break;
                case ConsoleKey.W: Throttle += 0.06f; break;
                case ConsoleKey.S: Throttle -= 0.06f; break;
                case ConsoleKey.Spacebar: Arm = !Arm; break;
                case ConsoleKey.M: Mode = (Mode + 1) % 3; break;
            }
            Roll = Math.Clamp(Roll, -1f, 1f);
            Pitch = Math.Clamp(Pitch, -1f, 1f);
            Yaw = Math.Clamp(Yaw, -1f, 1f);
            Throttle = Math.Clamp(Throttle, 0f, 1f);
        }

        public void Decay()
        {
            Roll *= 0.82f; Pitch *= 0.82f; Yaw *= 0.82f;   // springs back to centre
        }

        public void Write(int[] ch)
        {
            ch[0] = 1500 + (int)(Roll * 500);
            ch[1] = 1500 + (int)(Pitch * 500);
            ch[2] = 1000 + (int)(Throttle * 1000);
            ch[3] = 1500 + (int)(Yaw * 500);
            ch[4] = Arm ? 2000 : 1000;
            ch[5] = Mode == 0 ? 1000 : Mode == 1 ? 1500 : 2000;
        }
    }
}

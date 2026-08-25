using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Settings;

namespace ChiakiNg.Session;

/// <summary>What a capture attempt ended as, so a caller can say which step failed.</summary>
public enum CaptureOutcome
{
    Recorded,
    NoRegisteredConsole,
    NotFound,
    WouldNotWake,
    SessionRefused,
    NothingRecorded,
    CouldNotWrite,
}

/// <summary>
/// PP297: the run that produces the recording, which is the step the whole block waits on.
///
/// PP323 tapped the four chokepoints, PP325 taught the redaction to name a field, PP326 joined the
/// tap to the format and PP327 gave it a flag. All of that arms a recording of a session somebody
/// else starts. Nothing in this tree ever started one - the Qt client is off, and the managed host
/// opens a window rather than a stream - so the capture stayed owed to a console for four tasks
/// after the console stopped being the missing piece.
///
/// This drives it end to end and prints each step, because every one of them can fail for a reason
/// about the room rather than about the code: the console is off, it is on another subnet, the
/// registration is stale. A step that fails silently here reads as "the port cannot connect".
///
/// IT IS NOT THE APPLICATION. No window, no decoder, no controller - a session started and left to
/// run for a few seconds while the tap writes down what crossed. What PP297 needs is the control
/// conversation and its opening, and both are complete long before a frame is worth looking at.
/// </summary>
public static class ExchangeCapture
{
    /// <summary>How long to hold the session open once it connects.</summary>
    public static TimeSpan Hold { get; } = TimeSpan.FromSeconds(12);

    /// <summary>How long to wait for a console to answer discovery.</summary>
    public static TimeSpan Discover { get; } = TimeSpan.FromSeconds(8);

    /// <summary>How long a woken console gets to reach "ready".</summary>
    public static TimeSpan Wake { get; } = TimeSpan.FromSeconds(45);

    /// <summary>
    /// The wake packet's credential: the registration key as a hexadecimal NUMBER, not as bytes.
    ///
    /// discoverymanager.cpp truncates at the first NUL, reads the rest as base 16 and refuses more
    /// than 8 characters. Reproduced rather than reinvented - a credential that is wrong wakes
    /// nothing and reports no error, because a wake packet is fire-and-forget UDP.
    /// </summary>
    public static bool TryWakeCredential(ReadOnlySpan<byte> registKey, out ulong credential)
    {
        credential = 0;

        int length = registKey.IndexOf((byte)0);
        ReadOnlySpan<byte> text = length < 0 ? registKey : registKey[..length];

        if (text.Length is 0 or > 8)
            return false;

        return ulong.TryParse(
            Encoding.ASCII.GetString(text), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
            out credential);
    }

    /// <summary>
    /// Every broadcast address this machine can reach a console on.
    ///
    /// 255.255.255.255 is not enough: Windows sends a limited broadcast out one interface, and a
    /// machine with a VPN or a Hyper-V switch will pick the wrong one. Each interface's own
    /// directed broadcast is computed instead, which is what reaches the subnet the console is on.
    /// </summary>
    public static IReadOnlyList<IPAddress> Broadcasts()
    {
        var found = new List<IPAddress>();

        foreach (System.Net.NetworkInformation.NetworkInterface nic in
                 System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                continue;

            foreach (System.Net.NetworkInformation.UnicastIPAddressInformation ip in
                     nic.GetIPProperties().UnicastAddresses)
            {
                if (ip.Address.AddressFamily != AddressFamily.InterNetwork || ip.IPv4Mask is null)
                    continue;

                byte[] address = ip.Address.GetAddressBytes();
                byte[] mask = ip.IPv4Mask.GetAddressBytes();
                if (mask.All(b => b == 0))
                    continue;

                var broadcast = new byte[4];
                for (var i = 0; i < 4; i++)
                    broadcast[i] = (byte)(address[i] | (byte)~mask[i]);

                var candidate = new IPAddress(broadcast);
                if (!found.Contains(candidate))
                    found.Add(candidate);
            }
        }

        return found;
    }

    /// <summary>Sends a discovery packet to one address, and says nothing about the answer.</summary>
    private static void Send(DiscoveryCommand command, IPAddress to, bool ps5, ulong credential)
    {
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            byte[] packet = Discovery.Packet(command, ps5, credential);
            udp.Send(packet, packet.Length, new IPEndPoint(to, Discovery.Port(ps5)));
        }
        catch (SocketException ex)
        {
            Console.Error.WriteLine($"[capture]   {to}: {ex.Message}");
        }
    }

    /// <summary>
    /// Looks for the console until it answers or the wait runs out.
    ///
    /// One service per broadcast address. The service both sends the search and reads the replies,
    /// so pointing it at a broadcast is what makes it find a console whose address nobody knows -
    /// which is the case here, since a registered host stores a MAC and never an address.
    /// </summary>
    private static DiscoveredConsole? Find(
        IReadOnlyList<IPAddress> broadcasts, string wantedName, TimeSpan within, ChiakiLog log)
    {
        DiscoveredConsole? found = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var services = new List<DiscoveryService>();

        try
        {
            foreach (IPAddress broadcast in broadcasts)
            {
                try
                {
                    services.Add(new DiscoveryService(broadcast.ToString(), consoles =>
                    {
                        foreach (DiscoveredConsole console in consoles)
                        {
                            if (console.Address is null || !seen.Add(console.Address))
                                continue;

                            Console.WriteLine(
                                $"[capture]   {console.Name} at {console.Address} - "
                                + $"{Discovery.HostStateString(console.State)}");

                            // The nickname is what a person recognises and what the registry
                            // stores. A console whose name does not match is still printed, so a
                            // run that finds the wrong one says so rather than timing out.
                            if (string.Equals(console.Name, wantedName, StringComparison.OrdinalIgnoreCase))
                                found = console;
                        }
                    }, pingMs: 1000, log: log));
                }
                catch (InvalidOperationException ex)
                {
                    Console.Error.WriteLine($"[capture]   {broadcast}: {ex.Message}");
                }
            }

            if (services.Count == 0)
                return null;

            DateTime until = DateTime.UtcNow + within;
            while (DateTime.UtcNow < until && found is null)
                Thread.Sleep(200);

            return found;
        }
        finally
        {
            foreach (DiscoveryService service in services)
                service.Dispose();
        }
    }

    /// <summary>
    /// The whole capture: find the console, wake it if it is asleep, run a session and write down
    /// what crossed.
    /// </summary>
    /// <param name="path">Where the recording goes.</param>
    /// <param name="nickname">Which registered console, or null for the only one.</param>
    public static CaptureOutcome Run(string path, string? nickname)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        ChiakiSession.LibInit();

        using var log = new ChiakiLog(
            ChiakiLogLevel.All & ~ChiakiLogLevel.Verbose,
            (level, text) => Console.WriteLine($"[{ChiakiLog.LevelChar(level)}] {text}"));

        // 1. The console, out of the store the Qt client registered it in.
        var store = new QSettingsStore();
        IReadOnlyList<RegisteredHost> hosts = store.RegisteredHosts();

        RegisteredHost? host = nickname is null
            ? hosts.FirstOrDefault()
            : hosts.FirstOrDefault(h => string.Equals(h.ServerNickname, nickname, StringComparison.OrdinalIgnoreCase));

        if (host is null)
        {
            Console.Error.WriteLine(hosts.Count == 0
                ? "[capture] no registered console - register one in the Qt client first."
                : $"[capture] no registered console called {nickname}. Known: {string.Join(", ", hosts.Select(h => h.ServerNickname))}");
            return CaptureOutcome.NoRegisteredConsole;
        }

        if (host.RpRegistKey is not { Length: > 0 } registKey || host.RpKey is not { Length: 16 } morning)
        {
            Console.Error.WriteLine($"[capture] {host.ServerNickname} has no usable registration - re-register it.");
            return CaptureOutcome.NoRegisteredConsole;
        }

        bool ps5 = host.Target >= 1_000_000;
        Console.WriteLine($"[capture] {host.ServerNickname}  mac={host.MacText}  target={host.Target}  ps5={ps5}");

        // 2. Where it is. A registered host stores a MAC, so this is the only way to an address.
        IReadOnlyList<IPAddress> broadcasts = Broadcasts();
        Console.WriteLine($"[capture] searching {string.Join(", ", broadcasts)}");

        if (Find(broadcasts, host.ServerNickname, Discover, log) is not { Address: string address } found)
        {
            Console.Error.WriteLine(
                $"[capture] {host.ServerNickname} did not answer. It is off, or on another subnet than this machine.");
            return CaptureOutcome.NotFound;
        }

        // 3. Awake, if it is not.
        if (found.State != DiscoveryHostState.Ready)
        {
            if (!TryWakeCredential(registKey, out ulong credential))
            {
                Console.Error.WriteLine("[capture] the registration key is not a wake credential - re-register.");
                return CaptureOutcome.WouldNotWake;
            }

            Console.WriteLine($"[capture] {address} is in standby - waking it");
            Send(DiscoveryCommand.Wakeup, IPAddress.Parse(address), ps5, credential);

            if (Find(broadcasts, host.ServerNickname, Wake, log)
                is not { Address: string woken, State: DiscoveryHostState.Ready })
            {
                Console.Error.WriteLine("[capture] it did not reach ready. Remote play may be disabled on it.");
                return CaptureOutcome.WouldNotWake;
            }

            address = woken;
        }

        Console.WriteLine($"[capture] {address} is ready");

        // 4. Armed BEFORE the session exists, so the session request is inside the recording.
        using var recorder = ExchangeRecorder.Start();

        using var connect = new ChiakiConnectInfo { Host = address, Ps5 = ps5 };
        connect.SetRegistKey(registKey);
        connect.SetMorning(morning);
        connect.SetVideoPreset(ChiakiVideoResolution.P720, ChiakiVideoFps.Fps60);
        connect.SetFlags(autoDowngrade: true, keyboard: false, dualSense: false, idrOnFecFailure: false);

        using ChiakiSession? session = ChiakiSession.TryCreate(connect, log, out ChiakiError created);
        if (session is null)
        {
            Console.Error.WriteLine($"[capture] the session would not build: {created}");
            return CaptureOutcome.SessionRefused;
        }

        using var connected = new ManualResetEventSlim(false);
        using var quit = new ManualResetEventSlim(false);
        string? quitReason = null;

        session.SetEventHandler(e =>
        {
            Console.WriteLine($"[capture] event: {e.Type}");

            if (e.Type == ChiakiEventType.Connected)
                connected.Set();

            if (e.Type == ChiakiEventType.Quit)
            {
                quitReason = ChiakiSession.QuitReasonString((int)e.QuitReason) ?? e.QuitReason.ToString();
                quit.Set();
            }
        });

        ChiakiError started = session.Start();
        if (started != ChiakiError.Success)
        {
            Console.Error.WriteLine($"[capture] the session would not start: {started}");
            return CaptureOutcome.SessionRefused;
        }

        // 5. Hold it open. Connected is what the control channel finishing looks like from here;
        // a quit before that is still worth writing down, because the exchange up to the refusal is
        // exactly what a replay of a failure needs.
        if (WaitHandle.WaitAny([connected.WaitHandle, quit.WaitHandle], Wake) == 0)
        {
            Console.WriteLine($"[capture] connected - holding for {Hold.TotalSeconds:0}s");
            quit.Wait(Hold);
        }
        else if (quitReason is not null)
        {
            Console.WriteLine($"[capture] the console ended it: {quitReason}");
        }

        session.Stop();
        Thread.Sleep(1000);

        // 6. What crossed.
        if (recorder.Recording.Entries.Count == 0)
        {
            Console.Error.WriteLine("[capture] nothing was tapped - the session never reached the wire.");
            return CaptureOutcome.NothingRecorded;
        }

        if (!recorder.TryWriteTo(path, out string message))
        {
            Console.Error.WriteLine($"[capture] {message}");
            return CaptureOutcome.CouldNotWrite;
        }

        Console.WriteLine($"[capture] {message}");
        return CaptureOutcome.Recorded;
    }
}

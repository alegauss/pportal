using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Settings;

namespace ChiakiNg.Session;

/// <summary>PP514: which recording a capture run produces. The session path is the same for both.</summary>
public enum SessionCaptureKind
{
    /// <summary>PP297's: the four framed channels, redacted, in the exchange format.</summary>
    Exchange,

    /// <summary>PP510's: takion's arrivals, with their times, in the datagram format.</summary>
    Datagrams,

    /// <summary>
    /// PP700's: the session DECODES, and what is written down is how many frames came out.
    ///
    /// The other two record what crossed the wire and deliberately run with no decoder - PP297
    /// needs the control conversation and nothing more. This one exists because that left the port
    /// with no path that decoded at all, and a stream that reaches the frame processor and stops
    /// looks exactly like a stream that works until somebody asks for a picture.
    /// </summary>
    Decode,
}

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
    /// <summary>
    /// How long to hold the session open once it connects, for a sample nobody asked a length for.
    ///
    /// PP526: no longer a constant of this class. A hold has to cover the capture's window or the
    /// window samples a session that already ended, so the two are settled together in
    /// <see cref="SampleWindow"/> and this is what that says for the default length.
    /// </summary>
    public static TimeSpan Hold => SampleWindow.Default.Hold;

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
    /// PP614: which address the session is pointed at - the console's, or something forwarding for
    /// it.
    ///
    /// Separated from <see cref="Run"/> because it is the whole of the decision and the rest of
    /// that method needs a console to reach. Blank is the same as absent: a flag given with no
    /// value is a caller who meant the console, and connecting to "" would fail somewhere far from
    /// the mistake.
    /// </summary>
    /// <param name="discovered">Where discovery found the console.</param>
    /// <param name="via">What to go through instead, or null for the console itself.</param>
    public static string ConnectAddress(string discovered, string? via)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(discovered);

        return string.IsNullOrWhiteSpace(via) ? discovered : via.Trim();
    }

    /// <summary>
    /// PP616: the value of <c>--via</c> that means "start a relay here and go through it".
    ///
    /// A word rather than an address, because the three things a relay run changes are decided
    /// together - who fills the capture, how wide it keeps, and where the session points - and a
    /// caller who typed a loopback address by hand would get the last of the three only.
    /// </summary>
    public const string RelayVia = "relay";

    /// <summary>Whether a `--via` value asks for the relay rather than naming somewhere to go.</summary>
    public static bool AsksForRelay(string? via)
        => via is not null && string.Equals(via.Trim(), RelayVia, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// PP700: which hardware decoder a Decode run asks for, or empty for software.
    ///
    /// Set by the caller before Run, because the decoder is created inside it and the choice is the
    /// only thing a PP76 comparison varies. A name the machine has no device for is refused by the
    /// C, which is how a run says the driver is missing rather than quietly decoding on the CPU.
    /// </summary>
    public static string DecoderName { get; set; } = string.Empty;

    /// <summary>What a Decode run produced, for the caller that reports it.</summary>
    public static ulong FramesDecoded { get; private set; }

    /// <summary>The pixel format that resolved, which is a fact about the machine.</summary>
    public static string DecoderPixelFormat { get; private set; } = string.Empty;

    /// <summary>PP700: how many of those frames were rendered into the shared texture.</summary>
    public static ulong FramesRendered { get; private set; }

    /// <summary>
    /// PP700: a console found, woken and ready, with what a session needs to reach it.
    /// </summary>
    /// <param name="Nickname">Its registered name.</param>
    /// <param name="Address">Where it answered from, after any wake.</param>
    /// <param name="Ps5">Which generation, which decides the discovery port and the wake packet.</param>
    /// <param name="RegistKey">The registration, which is also the wake credential.</param>
    /// <param name="Morning">The sixteen bytes a session opens with.</param>
    public readonly record struct ReadyConsole(
        string Nickname, string Address, bool Ps5, byte[] RegistKey, byte[] Morning);

    /// <summary>
    /// PP700: find a registered console and wake it, which both runs need and only one had.
    ///
    /// Lifted out of Run rather than copied into StreamRun. The steps are the ones a session cannot
    /// start without and each fails for a reason about the room rather than the code - the console
    /// is off, it is on another subnet, the registration is stale - so a second copy would be a
    /// second set of sentences for the same three rooms.
    /// </summary>
    /// <returns>Null where any step failed, having said which on the error stream.</returns>
    public static ReadyConsole? FindAndWake(string? nickname, ChiakiLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        var store = new QSettingsStore();
        IReadOnlyList<RegisteredHost> hosts = store.RegisteredHosts();

        RegisteredHost? host = nickname is null
            ? hosts.FirstOrDefault()
            : hosts.FirstOrDefault(h => string.Equals(h.ServerNickname, nickname, StringComparison.OrdinalIgnoreCase));

        if (host is null)
        {
            Console.Error.WriteLine(hosts.Count == 0
                ? "[console] no registered console - register one in the Qt client first."
                : $"[console] no registered console called {nickname}. Known: {string.Join(", ", hosts.Select(h => h.ServerNickname))}");
            return null;
        }

        if (host.RpRegistKey is not { Length: > 0 } registKey || host.RpKey is not { Length: 16 } morning)
        {
            Console.Error.WriteLine($"[console] {host.ServerNickname} has no usable registration - re-register it.");
            return null;
        }

        bool ps5 = host.Target >= 1_000_000;
        IReadOnlyList<IPAddress> broadcasts = Broadcasts();

        if (Find(broadcasts, host.ServerNickname, Discover, log) is not { Address: string address } found)
        {
            Console.Error.WriteLine(
                $"[console] {host.ServerNickname} did not answer. It is off, or on another subnet than this machine.");
            return null;
        }

        if (found.State != DiscoveryHostState.Ready)
        {
            if (!TryWakeCredential(registKey, out ulong credential))
            {
                Console.Error.WriteLine("[console] the registration key is not a wake credential - re-register.");
                return null;
            }

            Console.WriteLine($"[console] {address} is in standby - waking it");
            Send(DiscoveryCommand.Wakeup, IPAddress.Parse(address), ps5, credential);

            if (Find(broadcasts, host.ServerNickname, Wake, log)
                is not { Address: string woken, State: DiscoveryHostState.Ready })
            {
                Console.Error.WriteLine("[console] it did not reach ready. Remote play may be disabled on it.");
                return null;
            }

            address = woken;
        }

        Console.WriteLine($"[console] {host.ServerNickname} at {address} is ready");
        return new ReadyConsole(host.ServerNickname, address, ps5, registKey, morning);
    }

    /// <summary>
    /// The whole capture: find the console, wake it if it is asleep, run a session and write down
    /// what crossed.
    /// </summary>
    /// <param name="path">Where the recording goes.</param>
    /// <param name="nickname">Which registered console, or null for the only one.</param>
    /// <param name="kind">
    /// PP514: which recording. Everything from finding the console to stopping the session is the
    /// same run - what differs is which tap is installed and what gets written at the end.
    ///
    /// A parameter and not a second command, because ChiakiMessageTap.Install REPLACES: two
    /// recorders in one session is one recorder and a silence. The default is Exchange, so
    /// --capture-exchange behaves as it did.
    /// </param>
    /// <param name="sample">
    /// PP526: how long a sample to take, or null for the default length.
    ///
    /// The window and the hold both come out of this, which is the whole reason it is one value:
    /// a run holding the session for twelve seconds while capturing sixty would report a window it
    /// never reached, and the file would not say so.
    /// </param>
    /// <param name="via">
    /// PP614: what to point the session at instead of the console, or null for the console itself.
    ///
    /// Undocumented until PP643 moved this block back onto the method it describes: the parameter
    /// arrived with PP614 and the docstring it should have joined was two declarations away.
    /// </param>
    public static CaptureOutcome Run(
        string path, string? nickname, SessionCaptureKind kind = SessionCaptureKind.Exchange,
        SampleBounds? sample = null, string? via = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        SampleBounds bounds = sample ?? SampleWindow.Default;

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
        // PP514: one of the two, never both - Install replaces, so a second tap is a silence.
        using ExchangeRecorder? recorder =
            kind == SessionCaptureKind.Exchange ? ExchangeRecorder.Start() : null;
        // PP616: through the relay, or through the tap. The two differ in three ways that go
        // together - who fills the capture, how much of each datagram it keeps, and where the
        // session is pointed - and a run that got one of the three wrong would record something
        // nobody could tell apart afterwards.
        bool relaying = AsksForRelay(via);

        using TakionCaptureWriter? datagrams = kind == SessionCaptureKind.Datagrams
            ? new TakionCaptureWriter(
                path,
                Monotonic,
                new TakionTimingCapture(
                    bounds,
                    relaying ? TakionTimingCapture.WholeDatagramBytes : TakionTimingCapture.HeadBytes),
                installTap: !relaying)
            : null;

        using SessionRelay? relay = relaying
            ? new SessionRelay(
                IPAddress.Parse(address),
                // Arrivals only. The tap records what the C RECEIVES, so a relay that also offered
                // the sends would produce a file the replay reads as twice the traffic.
                (bytes, fromConsole) =>
                {
                    if (fromConsole)
                        datagrams?.Capture.Offer(bytes, Monotonic(), bytes.Length);
                })
            : null;

        relay?.Start();

        if (relaying)
            Console.WriteLine($"[capture] relaying to {address}; whole datagrams, not the tap's head");

        // PP614: where the session is POINTED, which is not always where the console is.
        //
        // The registration keys are the console's and have to be; the address does not. PP613's
        // relay forwards for a console it is told about, so a capture that could only reach the
        // discovered address had no way to sit behind one. Printed rather than assumed, because a
        // relay's far side is the address above and a session that took the wrong one is silent.
        string target = relaying ? SessionRelay.Via : ConnectAddress(address, via);

        if (!string.Equals(target, address, StringComparison.Ordinal))
            Console.WriteLine($"[capture] going through {target}; the console is at {address}");

        using var connect = new ChiakiConnectInfo { Host = target, Ps5 = ps5 };
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

        // PP700: THE JOIN, and it goes here for a reason the field's own note gives - the stream
        // connection's thread reads video_sample_cb, so attaching after Start is a race whose
        // losing side is a session that decodes nothing and says so no differently.
        //
        // H264 because the connect above asks for 720p60 and nothing negotiates HEVC on this path
        // yet; when it does, the codec comes off the streaminfo event and this line moves with it.
        using SessionDecoder? decoder = kind == SessionCaptureKind.Decode
            ? new SessionDecoder(log.Handle, codec: 0, maxFps: 60, DecoderName)
            : null;

        if (decoder is not null)
        {
            if (!SessionDecoder.AttachTo(session.Handle, decoder.Handle))
            {
                Console.Error.WriteLine("[capture] the decoder would not attach to the session");
                return CaptureOutcome.SessionRefused;
            }

            Console.WriteLine(
                $"[capture] decoding with "
                    + $"{(DecoderName.Length == 0 ? "software" : DecoderName)}, "
                    + $"format {decoder.PixelFormatName}"
                    + (decoder.CopiesEveryFrame ? " (copied per frame)" : " (no copy)"));
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
            Console.WriteLine($"[capture] connected - holding for {bounds.Hold.TotalSeconds:0}s");

            if (decoder is not null)
                DrainToScreen(decoder, quit, bounds.Hold);
            else
                quit.Wait(bounds.Hold);
        }
        else if (quitReason is not null)
        {
            Console.WriteLine($"[capture] the console ended it: {quitReason}");
        }

        session.Stop();
        Thread.Sleep(1000);

        // PP700: read BEFORE the decoder is disposed, and reported whatever the count is. Zero
        // after a connected session is the state this line exists about, and it is a result rather
        // than a failure to run - so it is printed and handed back, not turned into an outcome.
        if (decoder is not null)
        {
            // FramesAvailable and NOT FramesDecoded, which is the opposite of PP76's choice for the
            // same reason: this run installs no reader, so nothing ever calls the pull, and the
            // codec's own frame_num would read zero on a session that decoded fine. What is being
            // asked here is whether the decoder produced, and the callback is what knows that.
            FramesDecoded = decoder.FramesAvailable;
            DecoderPixelFormat = decoder.PixelFormatName;

            Console.WriteLine(
                $"[capture] {FramesDecoded} frame(s) decoded as {DecoderPixelFormat}, "
                    + $"{FramesRendered} drawn"
                    + (FramesDecoded == 0 ? " - the session decoded nothing" : string.Empty));

            // A Decode run arms neither recorder, so it returns here rather than falling into the
            // two below - both of which read a tap this kind never installed.
            return FramesDecoded > 0 ? CaptureOutcome.Recorded : CaptureOutcome.NothingRecorded;
        }

        // 6. What crossed. PP514: two lines differ, and this is both of them.
        if (datagrams is not null)
            return WriteDatagrams(datagrams, path);

        // Exactly one of the two is non-null, by the switch that made them - which the compiler
        // cannot see, because `kind` is not what it is testing here.
        if (recorder!.Recording.Entries.Count == 0)
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

    /// <summary>
    /// PP700: pull decoded frames and render them, until the session quits or the hold runs out.
    ///
    /// THE PRESENTER IS BUILT ON THE FIRST FRAME and not before, because its size is the picture's
    /// and the picture's size is not known until one arrives. The console negotiates a profile and
    /// can change it mid-session; a presenter built from the connect info's request would be right
    /// until the first downgrade and wrong silently after it.
    ///
    /// A SOFTWARE FRAME IS REPORTED AND NOT DRAWN. yuv420p is three planes and the presenter takes
    /// two, and converting here would put a converter nobody measured between a decoder and the
    /// numbers a run reports about it.
    ///
    /// The loop polls rather than waiting on a signal. The decoder's frame-available callback runs
    /// on the stream connection's own thread, and rendering from there would put a GPU submit
    /// inside the packet path - which is the shape PP48's per-frame copy already costs enough of.
    /// </summary>
    private static void DrainToScreen(SessionDecoder decoder, ManualResetEventSlim quit, TimeSpan hold)
    {
        using RenderDevice? device = ChiakiRender.CreateD3d11();

        if (device is null)
        {
            Console.WriteLine("[capture] no D3D11 device - decoding without drawing");
            quit.Wait(hold);
            return;
        }

        SharedSurface? surface = null;
        VideoPresenter? presenter = null;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int refused = 0;

        try
        {
            while (!quit.IsSet && clock.Elapsed < hold)
            {
                if (!decoder.Pull(out SessionDecoder.DecodedFrame frame))
                {
                    // No frame at all leaves the width at zero; a frame the presenter cannot take
                    // reports its size, and saying which is the whole point of the distinction.
                    if (frame.Width > 0)
                    {
                        if (refused == 0)
                        {
                            Console.WriteLine(
                                $"[capture] the pulled frame is {frame.Width}x{frame.Height} in "
                                    + $"{SessionDecoder.NameOfFormat(frame.Format)}, "
                                    + "which the presenter cannot take");
                        }

                        refused++;
                    }

                    Thread.Sleep(2);
                    continue;
                }

                if (presenter is null)
                {
                    surface = SharedSurface.Create(device, frame.Width, frame.Height, out ShareStage shared);

                    if (surface is null)
                    {
                        Console.WriteLine($"[capture] the surface stopped at {shared}");
                        break;
                    }

                    presenter = VideoPresenter.Create(
                        device, surface, frame.Width, frame.Height, out RenderStage built);

                    if (presenter is null)
                    {
                        Console.WriteLine($"[capture] the presenter stopped at {built}");
                        break;
                    }

                    Console.WriteLine($"[capture] drawing {frame.Width}x{frame.Height}");
                }

                presenter.Render(
                    frame.Luma, frame.LumaStride, frame.Chroma, frame.ChromaStride, out _);
            }

            FramesRendered = presenter?.Frames ?? 0;

            if (refused > 0)
                Console.WriteLine($"[capture] {refused} frame(s) were not NV12 and were not drawn");
        }
        finally
        {
            presenter?.Dispose();
            surface?.Dispose();
        }
    }

    /// <summary>
    /// PP514: the datagram run's ending - dispose to flush, then say what landed.
    ///
    /// Disposed here rather than left to the `using`, because the file has to exist before this
    /// reports on it. Disposing twice is one write, which PP512 asserts.
    /// </summary>
    private static CaptureOutcome WriteDatagrams(TakionCaptureWriter writer, string path)
    {
        TakionTimingCapture capture = writer.Capture;
        int count = capture.Datagrams.Count;

        writer.Dispose();

        if (count == 0)
        {
            Console.Error.WriteLine(
                "[capture] no datagram was tapped - the session never reached the stream.");
            return CaptureOutcome.NothingRecorded;
        }

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"[capture] the capture could not be written to {path}");
            return CaptureOutcome.CouldNotWrite;
        }

        double? gap = TakionCaptureReplay.MeanGapMicroseconds(capture.Datagrams);

        Console.WriteLine(
            $"[capture] {count} datagram(s) over {capture.End} "
            + $"of a {capture.WindowMicroseconds / 1_000_000.0:0.#}s sample, "
            + (gap is null ? "no spacing" : $"mean gap {gap:0} us")
            + $", {capture.Missed} after the bound - written to {path}");

        return CaptureOutcome.Recorded;
    }

    /// <summary>
    /// A monotonic reading in microseconds, which is what PP510's arrivals are relative to.
    ///
    /// Stopwatch and not DateTime: the capture's own rule is that only differences are used, and a
    /// wall clock can step backwards mid-session.
    /// </summary>
    private static long Monotonic()
        => System.Diagnostics.Stopwatch.GetTimestamp() * 1_000_000L
            / System.Diagnostics.Stopwatch.Frequency;
}

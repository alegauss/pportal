using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Views;

namespace ChiakiNg.Session;

/// <summary>
/// PP700: a window with a console in it, which is the whole line's point.
///
/// Every piece was built and measured and none was joined. This is the join: a session with a
/// decoder on it (PP700's first slice), a presenter holding the renderer between frames (its
/// second), a shared surface WPF takes (PP131-PP135), the composed overlay PP10 wrote and PP319
/// arranged - and a D3DImage assigned to the property that has been bound and empty since PP10.
///
/// IT IS DELIBERATELY NOT THE APPLICATION. No console list, no settings, no controller: a window,
/// a picture, and a hold. PP13's front door is where a person picks a console, and joining that to
/// this is the next thing rather than this thing - what PP700 owes is a decoded console frame on
/// screen, and mixing the two would make a failure ambiguous between them.
///
/// THE THREADS ARE THE CARE HERE. The session's frames arrive on libchiaki's thread, the pull and
/// the render happen on a thread of this run's own, and the D3DImage belongs to the UI thread. The
/// middle one exists so a GPU submit is never inside the packet path and never inside a redraw.
/// </summary>
public static class StreamRun
{
    /// <summary>How long to wait for the console to answer before giving up.</summary>
    public static TimeSpan Wake { get; } = TimeSpan.FromSeconds(45);

    /// <summary>PP699: what the presenter did with each frame, which is PP76's first operand.</summary>
    public static PresentationCount Counted { get; private set; } = new();

    /// <summary>And the receiver's own total, which is the second.</summary>
    public static int Lost { get; private set; }

    /// <summary>PP76: decoded frames the pull's own drain threw away, which the C counts nowhere.</summary>
    public static int Superseded { get; private set; }

    /// <summary>
    /// PP76: what the decoder had produced AT THE MOMENT the loop stopped.
    ///
    /// Read there rather than after, because the session keeps decoding until it is stopped and a
    /// total taken later counts frames the reader was never offered. Five of them, in the run that
    /// found this - a residue small enough to look like rounding and misleading for that reason.
    /// </summary>
    public static ulong DecodedAtExit { get; private set; }

    /// <summary>
    /// Open a window and run one session into it, returning when the window closes.
    /// </summary>
    /// <param name="nickname">Which registered console, or null for the only one.</param>
    /// <param name="decoderName">A hardware decoder's name, or empty for software.</param>
    /// <param name="hold">
    /// How long to hold the window open, or null to leave it until a person closes it.
    ///
    /// A bound exists so a run can be STARTED and READ by something that is not a person - which is
    /// what a check needs and what a person watching does not.
    /// </param>
    /// <param name="microphone">
    /// PP825: whether to open one. Off unless asked for, which is the rule the phase's own property
    /// states - a client that recorded a room because a session started would be worse than one
    /// with no microphone at all.
    /// </param>
    /// <param name="audioPrefill">
    /// PP833: how many decoded frames to hold before the first submit. Zero is what shipped before
    /// it and what a caller who says nothing gets - the number is a latency trade and is measured
    /// against PP814's dry counter rather than chosen.
    /// </param>
    /// <param name="wirePath">
    /// PP834: where to write both directions of the relay, or null to write none. Needs `--via`,
    /// because a run that is not relayed has no wire this side can see.
    /// </param>
    public static int Run(
        string? nickname, string decoderName, TimeSpan? hold = null,
        string? via = null, string? capturePath = null, bool microphone = false,
        int audioPrefill = 0, string? wirePath = null)
    {
        ArgumentNullException.ThrowIfNull(decoderName);

        var model = new StreamOverlayViewModel { Loading = true };

        var window = new Window
        {
            Title = "ChiakiNg",
            Width = 1280,
            Height = 720,
            Background = System.Windows.Media.Brushes.Black,
            Content = new StreamOverlayView { DataContext = model },
        };

        // PP836: and the state machine PP11 ported, with a window to drive at last. Seeded with the
        // size above because EnterFullscreen pins the minimum to whatever Size says at that moment,
        // and a state that believes it is 1280x720 when the window is not would pin the wrong one.
        var chrome = new StreamWindowState();
        chrome.Resize((int)window.Width, (int)window.Height);

        // The adjustable flag comes back a second after leaving fullscreen and NOT on the way out,
        // which is the third of PP11's findings. A timer is what that second is; it is stopped and
        // restarted per leave, so two toggles inside the second do not race two timers.
        var settling = new DispatcherTimer { Interval = StreamWindowState.AdjustableDelay };

        settling.Tick += (_, _) =>
        {
            settling.Stop();
            chrome.AdjustableDelayElapsed();
            StreamWindowChrome.Apply(window, chrome);
        };

        // Tracked rather than assumed: the way out of fullscreen restores whatever the window was
        // before it, and a person maximizing it mid-stream is the case that gets that wrong.
        window.StateChanged += (_, _) =>
        {
            if (!chrome.Fullscreen)
                chrome.SetMaximized(window.WindowState == WindowState.Maximized);
        };

        window.SizeChanged += (_, e) =>
        {
            if (!chrome.Fullscreen)
                chrome.Resize((int)e.NewSize.Width, (int)e.NewSize.Height);
        };

        // PreviewKeyDown, so the toggle is seen before the overlay's own focus chain can take it.
        window.PreviewKeyDown += (_, e) =>
        {
            if (!StreamWindowShortcut.TogglesFullscreen(
                    StreamWindowShortcut.Resolve(e.Key, e.SystemKey), Keyboard.Modifiers))
            {
                return;
            }

            chrome.Toggle();
            StreamWindowChrome.Apply(window, chrome);

            settling.Stop();

            if (chrome.AdjustableAfter is not null)
                settling.Start();

            e.Handled = true;
        };

        var stopping = new CancellationTokenSource();
        int result = 1;

        window.Loaded += (_, _) =>
        {
            var worker = new Thread(() =>
            {
                result = Pump(
                    nickname, decoderName, model, window, stopping.Token,
                    new RunOptions(via, capturePath, microphone, audioPrefill, wirePath));

                // Closed from here rather than left open on a failure: a run with a bound is one
                // nobody is watching, and a window that outlived its session would hold it forever.
                window.Dispatcher.BeginInvoke(window.Close);
            })
            {
                IsBackground = true,
                Name = "stream",
            };

            worker.Start();

            if (hold is { } bound)
                stopping.CancelAfter(bound);
        };

        window.Closed += (_, _) =>
        {
            settling.Stop();
            stopping.Cancel();
        };

        window.ShowDialog();
        stopping.Cancel();
        return result;
    }

    /// <summary>
    /// PP833: the four the run takes and the pump only forwards, kept together.
    ///
    /// Each of these arrived as a separate parameter and the list had outgrown what a reader can
    /// hold: none of them is read by <see cref="Run"/> itself, so the shape that says so is one
    /// argument carrying the four rather than four the signature has to spell twice.
    /// </summary>
    /// <param name="Via">An address forwarding for the console, or null to dial it directly.</param>
    /// <param name="CapturePath">Where to record the datagrams, or null to record none.</param>
    /// <param name="Microphone">Whether to open one, which is off unless asked.</param>
    /// <param name="AudioPrefill">How many decoded frames to hold before the first submit.</param>
    /// <param name="WirePath">
    /// PP834: where to write BOTH directions of the relay, or null to write none. Beside the
    /// capture rather than into it: a replay corpus holds arrivals alone on purpose.
    /// </param>
    private readonly record struct RunOptions(
        string? Via, string? CapturePath, bool Microphone, int AudioPrefill, string? WirePath);

    /// <summary>
    /// The session, the decoder, the presenter and the pump, on a thread of their own.
    /// </summary>
    /// <remarks>
    /// The window is used only to reach its dispatcher and to set the model's properties, both of
    /// which marshal. Nothing here touches a control.
    /// </remarks>

    private static int Pump(
        string? nickname, string decoderName,
        StreamOverlayViewModel model, Window window, CancellationToken stopping,
        RunOptions options)
    {
        string? via = options.Via;
        string? capturePath = options.CapturePath;
        bool microphone = options.Microphone;

        ChiakiSession.LibInit();

        using var log = new ChiakiLog(
            ChiakiLogLevel.All & ~ChiakiLogLevel.Verbose,
            (level, text) => Console.WriteLine($"[{ChiakiLog.LevelChar(level)}] {text}"));

        if (ExchangeCapture.FindAndWake(nickname, log) is not { } found)
        {
            Say(window, model, "No registered console answered.");
            return 1;
        }

        // PP813: THROUGH A RELAY, AND ONLY THIS PATH INSTALLS THE PORT'S SENKUSHA. The capture flags
        // run the C's senkusha, so every recording this tree had was of the half that works. §PP813
        // ends asking for a capture on senkusha's own port and calls it a pktmon job wanting
        // elevation; PP829 made the relay carry that port, and this is what points a PORT run at it.
        bool relaying = ExchangeCapture.AsksForRelay(via);

        using TakionCaptureWriter? datagrams = relaying && capturePath is not null
            ? new TakionCaptureWriter(
                capturePath,
                ExchangeCapture.Monotonic,
                new TakionTimingCapture(
                    SampleWindow.Default, TakionTimingCapture.WholeDatagramBytes),
                installTap: false)
            : null;

        // PP834: the second artefact, which holds what this machine SENT. The corpus below still
        // takes arrivals alone - a replay stands in for a console and reads two directions as twice
        // the traffic - so the sends go to a file of their own or to none at all.
        using WireLog? wire = relaying && options.WirePath is { } wirePath
            ? new WireLog(wirePath, ExchangeCapture.Monotonic)
            : null;

        using SessionRelay? relay = relaying
            ? new SessionRelay(
                System.Net.IPAddress.Parse(found.Address),

                (bytes, fromConsole, leg) =>
                {
                    // Arrivals only into the corpus, as the capture's own relay path takes them.
                    if (fromConsole)
                        datagrams?.Capture.Offer(bytes, ExchangeCapture.Monotonic(), bytes.Length);

                    // And both directions into the wire log, which is what PP813 had none of.
                    wire?.Offer(bytes, fromConsole, leg);
                })
            : null;

        relay?.Start();

        using var legs = new SessionRelay.RelayLegReport(relay);

        string target = relaying ? SessionRelay.Via : found.Address;

        if (relaying)
            Console.WriteLine($"[stream] going through {target}; the console is at {found.Address}");

        using var connect = new ChiakiConnectInfo { Host = target, Ps5 = found.Ps5 };
        connect.SetRegistKey(found.RegistKey);
        connect.SetMorning(found.Morning);
        connect.SetVideoPreset(ChiakiVideoResolution.P720, ChiakiVideoFps.Fps60);
        // PP700: idrOnFecFailure TRUE, which is the one flag this run does not share with a capture.
        //
        // The captures set it false because they record a conversation and never show a picture, so
        // recovery costs bandwidth for nothing. A viewing session is the opposite: when FEC cannot
        // rebuild a frame, every P-frame after it references something that never arrived, and the
        // error spreads until the next keyframe - which is seconds away. Asking for one immediately
        // is what turns a lasting smear into a blink.
        //
        // The Qt client defaults it off, and that default is for a client whose loss is rarer.
        connect.SetFlags(autoDowngrade: true, keyboard: false, dualSense: false, idrOnFecFailure: true);

        using ChiakiSession? session = ChiakiSession.TryCreate(connect, log, out ChiakiError created);
        if (session is null)
        {
            Say(window, model, $"The session would not build: {created}");
            return 1;
        }

        using var decoder = new SessionDecoder(log.Handle, codec: 0, maxFps: 60, decoderName);

        // PP783: THE PORT DRIVES THE STREAM, and this is the path that installs it.
        //
        // PP696 handed session.c's stream phase to a callback and no file put one there, so a live
        // session reached the phase and stopped. PP763 put the C's run back and PP764 named the four
        // states; the tree was on TheC because this call did not exist. It moves to ThePort here.
        //
        // BEFORE Start, and that is not a style. The session thread reaches the stream phase on its
        // own and does not ask twice, so a handover installed afterwards races the one moment it
        // looks - the same rule SetEventHandler and the decoder's own door already follow.
        //
        // SessionDecoder.AttachTo is GONE from this path rather than left inert. It installs the sink
        // on the SESSION, so what decodes through it is the C's run calling the C's callback - and
        // the C no longer runs the stream. PP787 opened the second door for exactly this: the phase
        // takes decoder.Sample and feeds the same decoder from the managed receiver. Two doors into
        // one decoder with only one of them driven is the two-facts-in-two-files shape PP764 exists
        // about, so the undriven one does not stay.
        // PP813: THE TARGET AND NOT THE CONSOLE, which is the line that made a relayed run record
        // nothing of the port. Both phases open their OWN takion, so pointing the C session at a
        // relay left them dialling the console directly - `relay carried 0 on the stream leg and 0
        // on senkusha's` while the stream ran perfectly and the wire went unwatched. The old note
        // here said the relay forwards ctrl and 9296 only; PP829 gave it senkusha's port too, so
        // the reason it named is gone and the phases can follow the session.
        using var baseline = new SessionBaseline();
        // PP825: and the microphone, where --microphone asked for one. The generation comes off the
        // console discovery answered with, which is the same fact the connect info above took: a
        // caller that knows which console it is talking to is the only one that can say whether the
        // mic head carries its twentieth byte.
        using var phase = new ManagedStreamPhase(
            session, IPAddress.Parse(target), decoder.Sample, baseline)
        {
            Microphone = microphone ? found.Ps5 : null,
        };

        // PP833: set before the phase starts, because the sink is handed to the decoder inside
        // Build and a prefill arriving after the first frame would hold nothing.
        phase.Sound.Prefill = options.AudioPrefill;

        phase.InstallOn();

        // PP809: AND THE PORT MEASURES THE LINK, which is the step before the one above.
        //
        // PP792 gave session.c a callback for senkusha and PP805 wired a takion's arrivals to the
        // arms; nothing installed one, so this path measured with the C and streamed with the port.
        // Installed on the same session and before the same Start, because the C reaches this phase
        // EARLIER - a few lines after ctrl reports a session id.
        //
        // THE C'S RUN IS STILL BEHIND IT, deliberately, which is where this differs from the stream
        // flip. session.c takes the other arm when nothing is installed, so a machine running any
        // other path measures exactly as it did - and PP784 is the line that removes it, after a
        // live link has said the two agree.
        using var senkusha = new ManagedSenkushaPhase(session, IPAddress.Parse(target));

        senkusha.InstallOn();

        // PP76: armed before the start, so no frame is decoded while the reader is still asleep.
        decoder.SignalWhenReady();

        using var connected = new ManualResetEventSlim(false);
        using var quit = new ManualResetEventSlim(false);

        session.SetEventHandler(e =>
        {
            if (e.Type == ChiakiEventType.Connected)
                connected.Set();
            if (e.Type == ChiakiEventType.Quit)
                quit.Set();
        });

        if (session.Start() != ChiakiError.Success)
        {
            Say(window, model, "The session would not start.");
            return 1;
        }

        if (WaitHandle.WaitAny([connected.WaitHandle, quit.WaitHandle], Wake) != 0)
        {
            Say(window, model, "The console did not connect.");
            return 1;
        }

        // PP701: the pad, opened after the console answered and before anything is drawn.
        //
        // NOT a reason to refuse the session. A machine with nothing plugged in still streams, and
        // that is the state every version of this before it shipped in - what was wrong was that it
        // was the only state available.
        // PP783: at the port's sender and no longer at the C session. The C's own body handed the
        // state to stream_connection.feedback_sender, and the flip leaves that call storing a value
        // nothing sends - so a pad wired to it would push 426 states into a struct nobody reads.
        using var pad = new PadFeed(phase.Pad);

        // PP835: OPENED OFF THE THREAD THAT DRAWS, which a user reported as the picture freezing.
        //
        // PadFeed.Start is allowed ten seconds for the SDL thread and five more to open a controller
        // on it, and the draw loop below used to begin only after it returned. A pad already plugged
        // in makes that instant and hides the order; one connected while the session is coming up
        // does not, and across that window nothing is presented while the receiver keeps decoding on
        // its own thread - the log fills with the codec's buffer being full and frames dropped to
        // make room, which is a picture that has stopped over a stream that has not.
        //
        // THE PICTURE IS WHAT THIS RUN IS FOR, so it does not wait on an input device. The name is
        // printed from the opening thread rather than here, so a pad that arrives late still says
        // when it arrived - which is the reading that told these two runs apart.
        Task<bool> opening = Task.Run(() =>
        {
            bool open = pad.Start();

            Console.WriteLine(
                open
                    ? $"[stream] pad: {pad.PadName}"
                    : "[stream] no pad - the picture will arrive and nothing will be sent back");

            return open;
        });

        var started = DateTimeOffset.UtcNow;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        int drawn = Draw(decoder, model, window, quit, stopping);

        clock.Stop();
        session.Stop();

        // PP783: what the port's run did, printed whatever it was. A flip that streams and a flip
        // that reaches the idle loop and draws nothing look identical from the frame counts alone -
        // both report zero - and these are the readings that tell them apart.
        phase.Join(TimeSpan.FromSeconds(2));

        IReadOnlyList<ArrivalReading> readings = phase.Arrivals?.Readings ?? [];

        Console.WriteLine(
            $"[stream] phase: outcome {phase.Outcome?.Error.ToString() ?? "none"}, "
                + $"connected {phase.Events?.Connected ?? 0}, "
                + $"{phase.Arrivals?.Seen ?? 0} arrival(s), {phase.Arrivals?.Handled ?? 0} handled"
                + (readings.Count > 0 ? $", last {readings[^1]}" : string.Empty));

        // PP824: and whether this session answered the console, which the counts above cannot say.
        // A run that acks nothing is either a stream carrying no control data or a root that left
        // the sink null - the defect this line exists to make visible - and both report the same
        // arrivals. `last` is the verdict rather than a number, because HeldBack is the queue
        // working and NoQueue is a handshake that never finished.
        Console.WriteLine(
            $"[stream] data acks: {phase.Arrivals?.Acked ?? 0} sent, "
                + $"last {phase.Arrivals?.LastAck?.Verdict.ToString() ?? "none attempted"}");

        // PP832: what the session ASKED the console for, which no line of a run used to say. The
        // five frame numbers below are all about frames arriving; a picture that is corrupted is
        // read back from this one, and a healthy-looking run with zero requests against a damaged
        // screen is the case that has no other reading.
        Console.WriteLine(
            $"[stream] keyframes: {phase.Outbound?.IdrRequests ?? 0} requested, "
                + $"{phase.Outbound?.IdrRequestsRefused ?? 0} refused, "
                + $"{phase.Outbound?.CorruptFrameReports ?? 0} corrupt-frame report(s), "
                + $"{phase.Outbound?.FecFailures ?? 0} FEC failure(s)");

        // PP825: and the microphone, where one was asked for. Silent otherwise, because a line
        // reporting zero on every run nobody asked would read as a broken feature rather than an
        // unused one.
        if (phase.Capture is { } capture)
        {
            Console.WriteLine(
                $"[stream] microphone: {capture.Opened}, {capture.Formatted} packet(s) formatted, "
                    + $"{capture.Sent} sent, canceller {(capture.Cleaning ? "on" : "off")}, "
                    // PP840: and whether the console's own audio is being quieted for it, which no
                    // other number in this report explains - the ducking is in the mixer, below
                    // everything XAudio2 counts.
                    + $"ducking {(capture.DuckingOptOut ? "off" : "ON")} "
                    + $"({capture.DuckingStopped} hr {capture.DuckingError:X8}), "
                    + $"last {capture.LastSend}");
        }

        // PP835: joined before anything reads the pad or the `using` disposes it. Start bounds
        // itself at fifteen seconds and a session can end sooner than that, so a run quit early
        // would otherwise tear down a PadFeed that is still opening a controller.
        opening.Wait(TimeSpan.FromSeconds(20));

        // PP783: whether the pad's sink found a sender, which the state count alone cannot say. A
        // run nobody touched pushes zero states, and so does a run whose sink was never armed - the
        // two are the same number and only one of them is a defect.
        // PP838: and what the pad ended up being, which the start-time line above cannot say. A run
        // that began with no pad can end with one now, so `adopted` separates a controller plugged
        // in mid-session and untouched from one that never arrived - which used to be the same
        // reading of zero.
        Console.WriteLine(
            $"[stream] pad sink: armed {phase.Pad.Active}, {pad.Pushed} state(s) pushed, "
                + $"holding {pad.PadName ?? "nothing"}, {pad.Adopted} adopted after the start");

        // PP834: and what the wire log holds, where one was asked for. `sent` is the half no
        // capture in this tree ever had - the number that would have ended PP813 in a reading
        // instead of a task.
        if (wire is { } recorded)
        {
            Console.WriteLine(
                $"[stream] wire: {recorded.Rows} datagram(s) recorded, {recorded.Sent} sent by this "
                    + $"machine, at {options.WirePath}");
        }

        // PP798: which units the decoder refused, which its own log line does not say.
        //
        // ffmpegdecoder.c prints the ERROR and nothing that identifies the unit, so a benign first
        // one arriving before the codec has its extradata and a corruption halfway through a
        // session are the same sentence. AfterAnyDecode is what separates them, and it is only
        // knowable at the moment the unit was offered.
        if (decoder.Refused > 0)
        {
            Console.WriteLine(
                $"[decoder] {decoder.Refused} of {decoder.Pushed} unit(s) refused: "
                    + string.Join("; ", decoder.Refusals));
        }

        // PP809: what the port measured, which is the one reading that says whether this session's
        // launch spec carried senkusha's numbers or PP28's fallback. The arrival counts are beside
        // it for PP773's reason: a run that reached no arm and one whose arms refused everything
        // both end in timeouts, and only the second is about the handlers.
        senkusha.Join(TimeSpan.FromSeconds(2));

        Console.WriteLine(
            $"[senkusha] {(senkusha.Outcome is { } ran ? $"{ran.Error}, rung {ran.Reading?.Rung.ToString() ?? "none"}" : "never ran")}"
                + $", {senkusha.Router?.Seen ?? 0} arrival(s), {senkusha.Router?.Handled ?? 0} handled"
                + $", session carries {Measured(session)}");

        // PP814: the sound, which this line reported in no number at all while carrying six for
        // video. A listener on 2026-09-07 heard the console's music correctly and clicking every one
        // to two seconds, and correct-content-with-gaps is a discontinuity rather than corruption.
        //
        // BOTH DIRECTIONS, because they are opposite faults a listener cannot tell apart. `cleared`
        // is the ring getting more than three buffers ahead and being dumped; `silent` is the engine
        // filled with nothing because the ring ran dry. A periodic click is made either way, and
        // which number moves decides which fix.
        // AND THE ANNOUNCED FORMAT, without which the frame counts are uninterpretable: seconds are
        // frames over the rate, and the rate is whatever STREAMINFO said rather than a constant this
        // side picks. Reading 376320 frames as 7.8s took a guess at 48000 that nothing here printed.
        // PP816: AND THE PATH IS THE ENGINE'S NOW, so most of those numbers no longer exist. There
        // is no ring to clear, no silence written to keep a pump fed and no break to count, because
        // nothing here decides how much of a frame to play. What is left is what was submitted, what
        // the engine has not finished, and whether it could be opened at all.
        //
        // PP814: AND WHETHER THE ENGINE EVER RAN OUT, which none of those say. A queue that empties
        // plays silence and then resumes, and that is the click a listener reports - while submitted,
        // decoded and concealed all read healthy, because nothing on this side was late. The depth at
        // the END of a run is what "still queued" is; these two are the depth THROUGHOUT it, which is
        // the difference between a stream that is fine and one that only finished fine.
        Console.WriteLine(
            $"[audio] {phase.Sound.Submitted} frame(s) submitted, {phase.Sound.Unplayable} unplayable"
                + $", device {phase.Sound.Opened?.ToString() ?? "never opened"}"
                + $" hr {phase.Sound.Render?.LastError.ToString("X8") ?? "none"}"
                + $", {phase.Sound.Render?.Queued ?? 0} still queued"
                + $", ran dry {phase.Sound.Render?.RanDry ?? 0} time(s)"
                + $", shallowest {phase.Sound.Render?.Shallowest ?? 0}"
                + $", {phase.Sound.Render?.SamplesPlayed ?? 0} sample(s) played"
                // PP833: the cushion this run was given, printed BESIDE the dry count because the
                // two are one reading: a dry count means nothing without the prefill it was
                // measured at, and a run comparing them has to know which was which.
                + $", prefill {phase.Sound.Prefill}"
                + $", announced {phase.Speaker?.Announced.ToString() ?? "nothing"}");

        // AND THE DECODER'S OWN VIEW, which is where a lost packet shows and the render's cannot.
        //
        // Opus CONCEALS a frame it was handed nothing for - it emits plausible audio rather than a
        // gap - so a stream losing packets keeps the ring full, keeps the engine fed, and still
        // sounds wrong. Two readings of this run reported 0 cleared and 1 break against a listener
        // hearing five clicks, which is that shape exactly: the fault never reaches the render.
        if (phase.Speaker is { } speaker)
        {
            Console.WriteLine(
                $"[audio] decoder: {speaker.Decoded} decoded, {speaker.Concealed} concealed"
                    + $", {speaker.Refused} refused, {speaker.BeforeAnyHeader} before any header"
                    + $", engine took {phase.Sound.Submitted}");
        }

        // PP813: and where the ones that did nothing went, which the two counts above cannot say.
        // A run that answered Unknown with every pong lost reads identically here to one whose
        // pongs were refused by the arm - Handled counts a refusal - so the tally is what tells a
        // header this walk rejected from a base type it has no arm for from a pong that never came.
        if (senkusha.Router is { } router)
            Console.WriteLine($"[senkusha] {router.Census()}");

        // PP76: every decoded frame is now in exactly one column, which is what makes the last
        // number the decoder's rather than this loop's. FrameLedger holds the arithmetic and the
        // three ways it was got wrong; a residue here is printed rather than left for a reader to
        // subtract out and wonder about.
        long residue = FrameLedger.Residue((long)DecodedAtExit, drawn, Counted.Dropped, Lost);

        Console.WriteLine(
            $"[stream] {DecodedAtExit} decoded, {drawn} shown, "
                + $"{Lost} lost by the network, {Superseded} superseded by the pull's drain, "
                + $"{Counted.Dropped} dropped, "
                + $"{Counted.DecoderDropsAgainst(Lost)} attributable to the decoder"
                + (residue != 0 ? $", {residue} unaccounted" : string.Empty));

        RecordBaseline(baseline, decoderName, started, clock.Elapsed);

        return drawn > 0 ? 0 : 1;
    }

    /// <summary>
    /// Pull, render, and tell WPF - until the session quits or the window closes.
    /// </summary>
    /// <remarks>
    /// The presenter is built on the FIRST frame, because its size is the picture's and the console
    /// negotiates that. One built from the connect info's request would be right until the first
    /// downgrade and silently wrong after it.
    /// </remarks>
    private static int Draw(
        SessionDecoder decoder, StreamOverlayViewModel model, Window window,
        ManualResetEventSlim quit, CancellationToken stopping)
    {
        using RenderDevice? device = ChiakiRender.CreateD3d11();
        if (device is null)
        {
            Say(window, model, "No D3D11 device.");
            return 0;
        }

        SharedSurface? surface = null;
        VideoPresenter? presenter = null;
        StreamPresentation? presentation = null;

        try
        {
            while (!quit.IsSet && !stopping.IsCancellationRequested)
            {
                bool pulled = decoder.Pull(out SessionDecoder.DecodedFrame frame);

                // PP699: PP528's shape, kept. The count comes off the pull and is ZEROED by it, so
                // every arm below has to carry it somewhere - and in the C two returns between the
                // pull and the present did not, which is the defect PP528 repaired. One place, and
                // it is before any branch that could return.
                Lost += frame.FramesLost;
                Counted.Lost(frame.FramesLost);

                // PP76: the frames the drain threw away, COUNTED rather than inferred. They decoded
                // and nobody will ever see them, which is what frames_dropped means - and the shim
                // is the only place the number exists, because the C's drain counts nothing.
                for (int i = 0; i < frame.Superseded; i++)
                    Counted.Discard();

                Superseded += frame.Superseded;

                if (!pulled)
                {
                    // A frame that arrived and cannot be shown is a DISCARD; no frame at all is
                    // not. The width is what tells them apart.
                    if (frame.Width > 0)
                        Counted.Discard();

                    // PP76: WAIT for the decoder rather than sleeping past it. The pull drains the
                    // codec and keeps only the last, counting none of the rest - so every tick this
                    // loop spends asleep is frames it can lose without anything saying so. The
                    // timeout is a liveness bound and not a poll: it exists so a session that
                    // stopped decoding still notices the quit.
                    decoder.Ready.WaitOne(TimeSpan.FromMilliseconds(50));
                    continue;
                }

                if (presenter is null)
                {
                    surface = SharedSurface.Create(device, frame.Width, frame.Height, out _);
                    if (surface is null)
                        return 0;

                    presenter = VideoPresenter.Create(device, surface, frame.Width, frame.Height, out _);
                    if (presenter is null)
                        return 0;

                    // The D3DImage is the UI thread's, so it is made there and handed back.
                    SharedSurface attached = surface;
                    presentation = window.Dispatcher.Invoke(() =>
                    {
                        var made = new StreamPresentation(frame.Width, frame.Height);
                        made.Attach(attached);
                        model.Video = made.Source;
                        model.Loading = false;
                        return made;
                    });
                }

                // Rendered INSIDE the lock rather than before it. See StreamPresentation.Present:
                // the shared texture has no fence, so drawing outside D3DImage's lock lets WPF
                // compose a half-written one.
                VideoPresenter drawing = presenter;

                bool shown = presentation?.Present(
                    () => drawing.Render(
                        frame.Luma, frame.LumaStride, frame.Chroma, frame.ChromaStride, out _)) ?? false;

                // The client's two arms, one per frame: presented, or discarded. A frame that
                // reached the presenter and did not reach the screen is the eviction PP76 reads.
                if (shown)
                    Counted.Present();
                else
                    Counted.Discard();
            }

            // Both totals of one instant, and both of one clock. The session decodes on for as
            // long as it takes to stop, so a count read after this is of frames the reader was
            // never offered - and FramesDecoded rather than FramesAvailable, because only the
            // former advances on this thread and can be equal to what this thread did with it.
            DecodedAtExit = decoder.FramesDecoded;
            return (int)(presentation?.Shown ?? 0);
        }
        finally
        {
            presenter?.Dispose();
            surface?.Dispose();
        }
    }

    /// <summary>
    /// PP809: the three numbers the session is carrying, read off the C rather than off the run.
    ///
    /// Which is the point of reading them here at all: the run reports what it MEASURED and the
    /// session holds what it will SPEND, and PP28's fallback is the case where those differ. A
    /// senkusha that failed leaves 1454/1454/1000 here and a rung short of the end there, and the
    /// two lines together are what say which happened.
    /// </summary>
    private static string Measured(Native.ChiakiSession session)
        => SessionBigMaterial.TransportOf(session) is { } transport
            ? $"mtu {transport.MtuIn}/{transport.MtuOut}, rtt {transport.RoundTripMicroseconds}us"
            : "nothing - the session has no transport numbers";

    /// <summary>
    /// PP699: one row per session, in the ledger both builds append to.
    ///
    /// qmlbackend.cpp's recordSessionBaseline, minus the parts that need a client. The path is the
    /// same file - chiaki_baseline.jsonl in the log directory - because a comparison between this
    /// host and the Qt build is exactly what a shared ledger is for, and a second file would make
    /// the two incomparable while looking like more data.
    ///
    /// A ROW THAT SILENTLY FAILS TO LAND reads afterwards as a session that never ran, which is the
    /// one way this record can mislead the comparison it exists for. The client says so in a
    /// comment and warns; so does this.
    ///
    /// PP783: THE RECORD IS TAKEN RATHER THAN MADE HERE, because the stream phase has been pushing
    /// its four stage timings into one since the run started. Building a second one at the end would
    /// write a row with those fields empty, and the comparison this ledger exists for is exactly the
    /// one that would then be missing half its evidence.
    /// </summary>
    private static void RecordBaseline(
        SessionBaseline baseline, string decoderName, DateTimeOffset started, TimeSpan duration)
    {
        try
        {
            baseline.SetStarted(started);
            baseline.SetDuration(duration);
            // PP1: one version, one place. The assembly's own, so a row cannot claim a build the
            // executable is not - which a literal here would eventually do.
            baseline.SetAppVersion(
                typeof(StreamRun).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
            baseline.SetVideo("h264", 1280, 720, 60, 0);
            baseline.SetConfig(
                decoderName.Length == 0 ? "none" : decoderName,
                "d3d11",
                packetLossMax: 0,
                idrOnFecFailure: true);

            baseline.SetMeasured(
                measuredBitrateMbps: 0,
                averagePacketLoss: 0,
                framesPresented: (ulong)Counted.Presented,
                framesLost: (ulong)Lost,
                framesDropped: (ulong)Counted.Dropped,
                networkRttUs: 0);

            ChiakiError wrote = baseline.AppendTo(SessionBaseline.LedgerPath);

            if (wrote != ChiakiError.Success)
            {
                Console.Error.WriteLine(
                    $"[stream] the baseline did not land in {SessionBaseline.LedgerPath}: {wrote}");
                return;
            }

            Console.WriteLine($"[stream] baseline recorded to {SessionBaseline.LedgerPath}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[stream] the baseline could not be written: {error.Message}");
        }
    }

    private static void Say(Window window, StreamOverlayViewModel model, string sentence)
    {
        Console.Error.WriteLine($"[stream] {sentence}");

        window.Dispatcher.Invoke(() =>
        {
            model.Loading = false;
            model.Error = true;
        });
    }
}





using System.Net;
using System.Windows;
using System.Windows.Controls;
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
    public static int Run(string? nickname, string decoderName, TimeSpan? hold = null)
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

        var stopping = new CancellationTokenSource();
        int result = 1;

        window.Loaded += (_, _) =>
        {
            var worker = new Thread(() =>
            {
                result = Pump(nickname, decoderName, model, window, stopping.Token);

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

        window.Closed += (_, _) => stopping.Cancel();

        window.ShowDialog();
        stopping.Cancel();
        return result;
    }

    /// <summary>
    /// The session, the decoder, the presenter and the pump, on a thread of their own.
    /// </summary>
    /// <remarks>
    /// The window is used only to reach its dispatcher and to set the model's properties, both of
    /// which marshal. Nothing here touches a control.
    /// </remarks>
    private static int Pump(
        string? nickname, string decoderName,
        StreamOverlayViewModel model, Window window, CancellationToken stopping)
    {
        ChiakiSession.LibInit();

        using var log = new ChiakiLog(
            ChiakiLogLevel.All & ~ChiakiLogLevel.Verbose,
            (level, text) => Console.WriteLine($"[{ChiakiLog.LevelChar(level)}] {text}"));

        if (ExchangeCapture.FindAndWake(nickname, log) is not { } found)
        {
            Say(window, model, "No registered console answered.");
            return 1;
        }

        using var connect = new ChiakiConnectInfo { Host = found.Address, Ps5 = found.Ps5 };
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
        using var baseline = new SessionBaseline();
        using var phase = new ManagedStreamPhase(
            session, IPAddress.Parse(found.Address), decoder.Sample, baseline);

        phase.InstallOn();

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
        Console.WriteLine(
            pad.Start()
                ? $"[stream] pad: {pad.PadName}"
                : "[stream] no pad - the picture will arrive and nothing will be sent back");

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

        // PP783: whether the pad's sink found a sender, which the state count alone cannot say. A
        // run nobody touched pushes zero states, and so does a run whose sink was never armed - the
        // two are the same number and only one of them is a defect.
        Console.WriteLine(
            $"[stream] pad sink: armed {phase.Pad.Active}, {pad.Pushed} state(s) pushed");

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





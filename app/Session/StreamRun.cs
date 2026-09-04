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

        if (!SessionDecoder.AttachTo(session.Handle, decoder.Handle))
        {
            Say(window, model, "The decoder would not attach.");
            return 1;
        }

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

        int drawn = Draw(decoder, model, window, quit, stopping);

        session.Stop();
        Console.WriteLine($"[stream] {decoder.FramesAvailable} decoded, {drawn} shown");

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

        int lost = 0;


        SharedSurface? surface = null;
        VideoPresenter? presenter = null;
        StreamPresentation? presentation = null;

        try
        {
            while (!quit.IsSet && !stopping.IsCancellationRequested)
            {
                if (!decoder.Pull(out SessionDecoder.DecodedFrame frame))
                {
                    lost += frame.FramesLost;
                    Thread.Sleep(2);
                    continue;
                }

                // PP528's counter, which the pull ZEROES - so it is added here or lost for good.
                // Reported because it is what tells a picture torn by the network from one torn by
                // this port, and those have opposite fixes.
                lost += frame.FramesLost;

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
                presentation?.Present(
                    () => drawing.Render(
                        frame.Luma, frame.LumaStride, frame.Chroma, frame.ChromaStride, out _));
            }

            Console.WriteLine($"[stream] {lost} frame(s) lost by the network");
            return (int)(presentation?.Shown ?? 0);
        }
        finally
        {
            presenter?.Dispose();
            surface?.Dispose();
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



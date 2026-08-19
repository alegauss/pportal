using System.Collections.Concurrent;

namespace ChiakiNg.Session;

/// <summary>How <see cref="SdlThread.Start"/> ended.</summary>
public enum SdlStart
{
    /// <summary>SDL is up and the pump is running.</summary>
    Started,
    /// <summary>SDL_Init returned a failure. <see cref="SdlThread.Error"/> says which.</summary>
    Failed,
    /// <summary>It did not answer inside the bound. See the note on the bound below.</summary>
    TimedOut,
}

/// <summary>
/// PP8: the thread that owns SDL, from SDL_Init to SDL_Quit with the poll loop in between.
///
/// This is the piece the section calls "how the events arrive". controllermanager.cpp runs the
/// poll off a QTimer at 4ms on the GUI thread, which works there because Qt's loop is the same
/// loop that draws. The port does not copy that. A WPF Dispatcher that has to service a 4ms timer
/// is a dispatcher competing with rendering for the one thread the UI cannot afford to lose, and
/// input arriving late is the failure people describe as the stream feeling worse than it is.
///
/// So the divergence is deliberate and it is this: a dedicated thread, and the interval is Qt's
/// own <see cref="PollIntervalMs"/> rather than a number chosen here.
///
/// One thread and not merely a background one. SDL's Windows joystick backend creates a
/// message-only window to hear about device arrival, and it belongs to whichever thread called
/// SDL_Init; SDL_PollEvent is what pumps that window's queue. Init on one thread and poll on
/// another and hotplug goes quiet - not with an error, just with a controller nobody notices was
/// plugged in. Quit belongs there too, for the same reason.
///
/// The bound on Start
/// ------------------
/// PP117: loading SDL2.dll used to hang forever, and the reason was an invisible modal error box
/// for a dependency Windows could not resolve - a dialog in a process with no window to put it
/// in. That is fixed at the resolver, where SetErrorMode now makes the load fail instead of wait.
/// The bound stays anyway. It costs one thread and it converts any future variant of that - a
/// driver, a device that scans on enumeration - from a host that never starts into a host that
/// starts without a gamepad and says so.
/// </summary>
public sealed class SdlThread : IDisposable
{
    /// <summary>controllermanager.cpp's UPDATE_INTERVAL_MS. Deliberately its number, not one here.</summary>
    public const int PollIntervalMs = 4;

    private readonly Thread thread;
    private readonly ConcurrentQueue<Action> posted = new();
    private readonly ManualResetEventSlim ready = new(false);
    private readonly CancellationTokenSource stopping = new();
    private readonly Action<SdlEvent>? onEvent;

    private volatile SdlStart outcome = SdlStart.TimedOut;
    private volatile string error = "";

    /// <summary>
    /// <paramref name="onEvent"/> is raised ON the SDL thread, once per event, and must not block:
    /// whatever it does happens between two polls. Marshalling to the dispatcher is the caller's
    /// job precisely because the caller knows which of the two consumers it is - the session,
    /// which wants the state now, or the mapping screen, which wants it on the UI thread.
    /// </summary>
    public SdlThread(Action<SdlEvent>? onEvent = null)
    {
        this.onEvent = onEvent;
        thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "ChiakiNg SDL",
        };
    }

    /// <summary>Whatever SDL last recorded, or the empty string. Never null.</summary>
    public string Error => error;

    /// <summary>The managed id of the thread that owns SDL, for a caller that wants to assert it.</summary>
    public int ThreadId => thread.ManagedThreadId;

    /// <summary>Whether the pump is running.</summary>
    public bool Running => outcome == SdlStart.Started && !stopping.IsCancellationRequested;

    /// <summary>
    /// Starts the thread and waits for SDL to answer, or for the bound to expire.
    ///
    /// A timeout does not kill the thread: it is a background thread, and something inside a
    /// native call cannot be interrupted anyway. What it does is give the caller back its own
    /// control flow, which is the whole point.
    /// </summary>
    public SdlStart Start(TimeSpan timeout)
    {
        thread.Start();
        return ready.Wait(timeout) ? outcome : SdlStart.TimedOut;
    }

    /// <summary>
    /// Runs <paramref name="work"/> on the SDL thread, between two polls.
    ///
    /// Anything that touches an SDL controller handle goes through here. SDL's game controller API
    /// is not documented as thread-safe against its own event pump, and a rumble written from the
    /// UI thread while the pump is inside SDL_PollEvent is the kind of thing that works until it
    /// is a crash report nobody can reproduce.
    /// </summary>
    public void Post(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        posted.Enqueue(work);
    }

    /// <summary>
    /// Posts <paramref name="work"/> and waits for it, returning false if the thread is not
    /// running or did not get to it inside <paramref name="timeout"/>.
    /// </summary>
    public bool Invoke(Action work, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (!Running)
            return false;

        using var done = new ManualResetEventSlim(false);
        Post(() =>
        {
            try { work(); }
            finally { done.Set(); }
        });
        return done.Wait(timeout);
    }

    /// <summary>
    /// Asks the pump to stop and waits for SDL_Quit to have run on its own thread. Idempotent:
    /// Dispose calls it, and a caller that already did is not punished for it.
    /// </summary>
    public void Stop(TimeSpan timeout)
    {
        if (!stopping.IsCancellationRequested)
            stopping.Cancel();
        if (thread.IsAlive)
            thread.Join(timeout);
    }

    public void Dispose()
    {
        Stop(TimeSpan.FromSeconds(5));
        stopping.Dispose();
        ready.Dispose();
    }

    private void Run()
    {
        try
        {
            outcome = Gamepads.Start() ? SdlStart.Started : SdlStart.Failed;
            error = Gamepads.Error();
        }
        catch (Exception ex)
        {
            // A DllNotFoundException from the resolver is the ordinary case on a tree with no
            // portable build. It is an outcome, not a crash on a background thread.
            outcome = SdlStart.Failed;
            error = ex.Message;
        }
        finally
        {
            ready.Set();
        }

        if (outcome != SdlStart.Started)
            return;

        try
        {
            Pump();
        }
        finally
        {
            // On this thread, because the message-only window that hotplug rides on is this
            // thread's. Anywhere else it is a quit that leaves the window behind.
            Gamepads.Stop();
        }
    }

    private void Pump()
    {
        while (!stopping.IsCancellationRequested)
        {
            while (posted.TryDequeue(out Action? work))
            {
                // A posted action that throws must not take SDL down with it: the pump outlives
                // any one caller, and a controller screen that asked for something impossible is
                // not a reason for the session to lose its input.
                try { work(); }
                catch (Exception ex) { error = ex.Message; }
            }

            while (Gamepads.PollEvent(out SdlEvent ev))
                onEvent?.Invoke(ev);

            // Sleep rather than spin. 4ms is Qt's interval and it is short enough that the
            // difference between sleeping it and polling it is a core. Waiting on the token and
            // not Thread.Sleep, so a Stop is answered at once rather than one interval later.
            stopping.Token.WaitHandle.WaitOne(PollIntervalMs);
        }
    }
}

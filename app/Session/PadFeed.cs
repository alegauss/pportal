using ChiakiNg.Native;

namespace ChiakiNg.Session;

/// <summary>
/// PP701: a real pad opened, translated and pushed at the session, for as long as one is held.
///
/// <see cref="PadTranslation"/> is the mapping and this is the wiring: SDL on its own thread, the
/// first game controller it enumerates, and a push on every change. What was missing before was
/// only ever this - the state, the shim's setter and the session's lock all existed and nothing
/// called them outside the self-test.
///
/// ONE PAD, THE FIRST ONE. The client folds every attached controller into a union with
/// chiaki_controller_state_or, and that is the right shape for a client somebody plays on. This is
/// the smallest thing that lets a person play, which is what PP76's reading needs; a second pad is
/// a feature and not a correction, and the union it needs is already exported and already tested.
///
/// PUSHED ON CHANGE. libchiaki's feedback sender reads whatever state it was last handed, on its
/// own timer, so pushing every event would push the same state repeatedly and pushing none would
/// leave the console holding a stale one. The change is the moment there is something new to say -
/// which is also controllermanager.cpp's rule, and the reason its handlers return a bool.
/// </summary>
public sealed class PadFeed : IDisposable
{
    private readonly PadTranslation translation = new();
    private readonly ChiakiControllerState state = new();
    private readonly ChiakiSession session;
    private readonly object gate = new();

    private SdlThread? sdl;
    private IntPtr controller;
    private long pushed;

    /// <param name="session">The session to push at. Not owned, and outlives this.</param>
    public PadFeed(ChiakiSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        this.session = session;
    }

    /// <summary>How many states have been pushed. Zero after a session is a pad that never reached it.</summary>
    public long Pushed => Interlocked.Read(ref pushed);

    /// <summary>The name of the pad that was opened, or null where none was.</summary>
    public string? PadName { get; private set; }

    /// <summary>
    /// Opens SDL and the first game controller on it, and reports whether a pad is now feeding.
    ///
    /// FALSE IS NOT A FAILURE OF THE SESSION. A machine with no pad plugged in still streams, and
    /// this returning false is how a caller says so rather than refusing to run - the picture is
    /// worth having without a controller, which is the state everything before this shipped in.
    /// </summary>
    public bool Start()
    {
        var thread = new SdlThread(Offer);

        if (thread.Start(TimeSpan.FromSeconds(10)) != SdlStart.Started)
        {
            thread.Dispose();
            return false;
        }

        sdl = thread;

        // Opened ON the SDL thread. Gamepads' own note: SDL's controller API is not documented as
        // thread-safe against its own pump, and this runs between two polls.
        thread.Invoke(
            () =>
            {
                foreach (SdlPad pad in Gamepads.Pads())
                {
                    IntPtr opened = Gamepads.OpenController(pad.Index);
                    if (opened == IntPtr.Zero)
                        continue;

                    controller = opened;
                    PadName = pad.Name;
                    break;
                }
            },
            TimeSpan.FromSeconds(5));

        return controller != IntPtr.Zero;
    }

    /// <summary>
    /// One event, folded in and pushed if it changed anything.
    ///
    /// Runs on the SDL thread, which is why it is short: SdlThread's own note says a callback
    /// happens between two polls and must not block. The push takes libchiaki's state lock and
    /// returns - it does not wait for the console.
    /// </summary>
    private void Offer(SdlEvent ev)
    {
        lock (gate)
        {
            if (!translation.Offer(ev))
                return;

            translation.WriteTo(state);
            session.SetControllerState(state);
        }

        Interlocked.Increment(ref pushed);
    }

    public void Dispose()
    {
        // The pad is closed on the thread that opened it, and BEFORE the pump stops - after it,
        // Invoke has nothing left to run on and the handle would leak with SDL still holding it.
        if (sdl is { Running: true } thread && controller != IntPtr.Zero)
        {
            thread.Invoke(
                () =>
                {
                    Gamepads.CloseController(controller);
                    controller = IntPtr.Zero;
                },
                TimeSpan.FromSeconds(5));
        }

        sdl?.Dispose();
        sdl = null;
        state.Dispose();
    }
}

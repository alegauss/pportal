namespace ChiakiNg.Session;

/// <summary>
/// PP217: the object that joins the mapping screen to the pad.
///
/// Every part of this screen was ported and none of it was connected. <see cref="MappingCapture"/>
/// (PP126) turns an event into a token, <see cref="ControllerMappingViewModel"/> (PP172) records
/// what the screen ASKED for, <see cref="ControllerMappingDocument"/> binds and rebuilds the
/// mapping string, and ControllerMappingView (PP173) draws it. This is what makes them one screen.
///
/// It works by draining the view model's own request log rather than by reaching into it. The
/// screen appends a <see cref="MappingRequest"/> for every call the QML would have made on the
/// backend; this fulfils the ones it has not fulfilled yet, in order. That keeps the screen exactly
/// as assertable as PP172 left it - it still records rather than does - and puts every side effect
/// in one switch that can be read at once.
///
/// THE THREAD SEAM, which is the reason this is a task and not a constructor.
///
/// controllermanager.cpp polls SDL from a QTimer on the GUI thread, so in the Qt client arming a
/// capture and taking a press happen on the same thread and no race is possible. PP8 moved the
/// poll onto a dedicated thread deliberately - a WPF dispatcher servicing a 4ms timer competes
/// with rendering - and the cost of that choice is paid here: <see cref="OpenCapture"/> arms from
/// the UI thread and <see cref="OnSdlEvent"/> takes from the SDL thread, on one flag. The gate
/// below is that ordering owned rather than hoped for.
///
/// And the marshal is a PARAMETER. Application.Current.Dispatcher would work in the application
/// and nowhere else, which is the shape PP37 filed against: a seam only a shown window can
/// exercise. Injected, the whole path is drivable from a test with no window, no pad and no SDL.
/// </summary>
public sealed class ControllerMappingSession
{
    private readonly ControllerMappingDocument document;
    private readonly MappingCapture capture;
    private readonly Action<Action> toUiThread;
    private readonly object gate = new();

    /// <summary>How many of the screen's requests have been acted on.</summary>
    private int fulfilled;

    /// <param name="document">The mapping being edited. Owned by the caller, mutated here.</param>
    /// <param name="toUiThread">
    /// Runs one piece of work where the screen's bindings live. Called from the SDL thread, so an
    /// implementation that runs the work inline is only correct in a test.
    /// </param>
    /// <param name="capture">The arm, for a caller that wants to hold one. A new one otherwise.</param>
    public ControllerMappingSession(
        ControllerMappingDocument document,
        Action<Action> toUiThread,
        MappingCapture? capture = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(toUiThread);

        this.document = document;
        this.toUiThread = toUiThread;
        this.capture = capture ?? new MappingCapture();

        Screen = new ControllerMappingViewModel();
        Screen.Show(document);
    }

    /// <summary>The screen, already filled from the document.</summary>
    public ControllerMappingViewModel Screen { get; }

    /// <summary>The mapping string the last apply produced, or null while none has.</summary>
    public string? Applied { get; private set; }

    /// <summary>Whether the screen has asked to leave mapping mode.</summary>
    public bool Finished { get; private set; }

    /// <summary>Whether a press would currently be taken. Read across threads, so read under the gate.</summary>
    public bool Armed
    {
        get { lock (gate) return capture.IsArmed; }
    }

    /// <summary>
    /// A row's button clicked: the screen opens its capture and the pad is armed for one press.
    /// The UI thread's call.
    /// </summary>
    public void OpenCapture(int buttonValue, int buttonIndex, int mappingIndex)
    {
        Screen.OpenCapture(buttonValue, buttonIndex, mappingIndex);
        Fulfil();
    }

    /// <summary>The capture dismissed without a press. The UI thread's call.</summary>
    public void CloseCapture()
    {
        Screen.CloseCapture();
        Fulfil();
    }

    /// <summary>Update pressed: apply the mapping and leave. The UI thread's call.</summary>
    public void Apply()
    {
        Screen.Apply();
        Fulfil();
    }

    /// <summary>The screen going away while mapping is still running.</summary>
    public void Destroyed()
    {
        Screen.Destroyed();
        Fulfil();
    }

    /// <summary>
    /// One SDL event, ON THE SDL THREAD.
    ///
    /// The offer is taken under the gate because the arm it clears is set from the UI thread. What
    /// follows is not: the screen's own state belongs to the dispatcher, so a token crosses back
    /// through <c>toUiThread</c> and nothing here touches a binding.
    /// </summary>
    public void OnSdlEvent(SdlEvent ev)
    {
        string? token;
        lock (gate)
            token = capture.Offer(ev);

        if (token is null)
            return;

        toUiThread(() =>
        {
            Screen.ButtonSelected(token);
            Fulfil();
        });
    }

    /// <summary>
    /// Acts on every request the screen has made and this has not answered, in order.
    ///
    /// Runs on the UI thread in every path: the three calls above are the UI's, and the fourth
    /// reaches here already marshalled.
    /// </summary>
    private void Fulfil()
    {
        IReadOnlyList<MappingRequest> requests = Screen.Requests;

        for (; fulfilled < requests.Count; fulfilled++)
        {
            MappingRequest request = requests[fulfilled];

            switch (request.Action)
            {
                case MappingAction.SelectButton:
                    lock (gate)
                    {
                        // Read here rather than held: the checkbox outlives any one capture, and
                        // what matters is what it said when this capture opened.
                        capture.AllowAnalogStick = Screen.EnableAnalogStickMapping;
                        capture.Arm();
                    }

                    break;

                case MappingAction.UpdateButton:
                    document.Assign(request.ButtonValue, request.PressedButton, request.ButtonIndex);

                    // Refilled from the document rather than patched in place, so the grid shows
                    // what would be serialised and not what the screen believes was bound.
                    Screen.Show(document);
                    break;

                case MappingAction.ButtonQuit:
                    lock (gate)
                        capture.Disarm();

                    break;

                case MappingAction.Apply:
                    Applied = document.Serialise();
                    break;

                case MappingAction.Quit:
                    Finished = true;
                    break;

                default:
                    break;
            }
        }
    }
}

/// <summary>
/// PP217: why this seam exists, read out of the client that does not have it.
/// </summary>
public static class MappingSessionSource
{
    /// <summary>The Qt client's controller code.</summary>
    public static string? Locate() => MappingSource.Locate();

    /// <summary>
    /// Whether the Qt client still polls SDL from a timer on the GUI thread. True means the race
    /// this session serialises is the PORT's alone - it is created by PP8's dedicated thread and
    /// has no counterpart to be faithful to.
    /// </summary>
    public static bool ThePollIsStillOnAGuiTimer(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source.Contains("auto timer = new QTimer(this);", StringComparison.Ordinal)
            && source.Contains(
                "connect(timer, &QTimer::timeout, this, &ControllerManager::HandleEvents);",
                StringComparison.Ordinal)
            && source.Contains("timer->start(UPDATE_INTERVAL_MS);", StringComparison.Ordinal);
    }

    /// <summary>
    /// And whether the interval PP8 copied is still the one it copied. PP8 names the number in a
    /// comment; nothing until now held it against the file it was taken from.
    /// </summary>
    public static bool TheIntervalIsStillTheOnePP8Copied(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Contains(
            $"#define UPDATE_INTERVAL_MS {SdlThread.PollIntervalMs}", StringComparison.Ordinal);
    }
}

namespace ChiakiNg.Session;

/// <summary>What the mapping screen asked the input subsystem to do, in order.</summary>
public enum MappingAction
{
    /// <summary>Arm the capture: the next press becomes a binding.</summary>
    SelectButton,

    /// <summary>A press arrived and a binding was written.</summary>
    UpdateButton,

    /// <summary>Stop waiting for a press, without having taken one.</summary>
    ButtonQuit,

    /// <summary>Apply the whole mapping to the device.</summary>
    Apply,

    /// <summary>Leave mapping mode.</summary>
    Quit,
}

/// <summary>One thing the screen asked for, with the arguments it passed.</summary>
/// <param name="Action">Which call.</param>
/// <param name="ButtonValue">The chiaki button being bound, for UpdateButton.</param>
/// <param name="PressedButton">The physical button that arrived, for UpdateButton.</param>
/// <param name="ButtonIndex">Which of the two slots on that row, for UpdateButton.</param>
public readonly record struct MappingRequest(
    MappingAction Action,
    int ButtonValue = 0,
    string PressedButton = "",
    int ButtonIndex = 0);

/// <summary>
/// PP18: the controller mapping screen, which is a state machine over a live event stream.
///
/// WHAT IS NOT ASSERTED HERE, said first because it is the point of the task. This screen IS the
/// event stream: it lights up whatever the user presses, and a port that draws it correctly with
/// no pad attached has proved nothing about the half that matters. Nothing below stands in for a
/// device. What it does is record the CALLS the screen makes, in order, so the sequence a real pad
/// would drive is assertable without one - and the sequence is where this screen's two defects
/// live.
///
/// Both are one-shot flags that reset themselves in the handler that consumed them:
///
/// 1. <c>quitButtonMapping</c> starts TRUE. Closing the capture dialog calls
///    <c>controllerMappingButtonQuit()</c> - unless a press arrived first, in which case the signal
///    handler sets the flag false so the close does NOT quit, and the close then sets it back to
///    true. A port that treated it as an ordinary boolean would suppress the quit for every capture
///    after the first, leaving the input path armed with nothing waiting for it.
///
/// 2. <c>resetFocus</c> is the same shape for where the focus goes on close.
///
/// The third rule is the one that is easy to read past: <c>onControllerMappingInProgressChanged</c>
/// closes the WHOLE screen when mapping stops - unless this screen is the thing stopping it, which
/// <c>finalClosing</c> says. Without that guard, closing the screen closes it again.
/// </summary>
public sealed class ControllerMappingViewModel : DialogViewModel
{
    private readonly List<MappingRequest> requests = [];

    private string controllerType = "";
    private bool altered;
    private bool enableAnalogStickMapping;
    private bool inProgress = true;
    private bool captureOpen;
    private bool closed;

    // The two one-shot flags, both starting true and both reset by the handler that reads them.
    private bool quitButtonMapping = true;
    private bool resetFocus = true;

    private bool finalClosing;
    private int captureButtonValue;
    private int captureButtonIndex;
    private int captureMappingIndex;

    protected override string ButtonProperty => nameof(CanApply);

    /// <summary>Everything the screen has asked for, in order. The assertion surface.</summary>
    public IReadOnlyList<MappingRequest> Requests => requests;

    /// <summary>The pad's own name, which is this screen's title and its prompt's middle.</summary>
    public string ControllerType
    {
        get => controllerType;
        set { Set(ref controllerType, value ?? ""); Raise(nameof(Title)); Raise(nameof(CapturePrompt)); }
    }

    /// <summary>Whether anything has been rebound, which is the only thing enabling Update.</summary>
    public bool Altered
    {
        get => altered;
        set => Set(ref altered, value);
    }

    /// <summary>Whether the sticks and triggers may be bound too - an opt-in, off by default.</summary>
    public bool EnableAnalogStickMapping
    {
        get => enableAnalogStickMapping;
        set => Set(ref enableAnalogStickMapping, value);
    }

    /// <summary>Whether the input subsystem is still in mapping mode.</summary>
    public bool InProgress => inProgress;

    /// <summary>Whether the capture dialog is up.</summary>
    public bool CaptureOpen
    {
        get => captureOpen;
        private set => Set(ref captureOpen, value);
    }

    /// <summary>Whether the screen itself has closed.</summary>
    public bool Closed
    {
        get => closed;
        private set => Set(ref closed, value);
    }

    /// <summary>The title, which is the pad's name and nothing else.</summary>
    public string Title => ControllerType;

    /// <summary>The Update button's rule.</summary>
    public bool CanApply => Altered;

    /// <summary>The capture dialog's prompt, with the pad's name in the middle of the sentence.</summary>
    public string CapturePrompt
        => $"Press any {ControllerType} button to map to DualSense controller button or click close";

    /// <summary>Where the focus goes when the capture closes, or null when it is not to move.</summary>
    public (int MappingIndex, int ButtonIndex)? FocusOnClose { get; private set; }

    /// <summary>
    /// Clicking one of the row's buttons: the capture opens and the input path is armed.
    /// </summary>
    public void OpenCapture(int buttonValue, int buttonIndex, int mappingIndex)
    {
        captureButtonValue = buttonValue;
        captureButtonIndex = buttonIndex;
        captureMappingIndex = mappingIndex;

        CaptureOpen = true;
        requests.Add(new MappingRequest(MappingAction.SelectButton));
    }

    /// <summary>
    /// A press arrived. The binding is written, the one-shot suppression is set, and the capture
    /// closes - in that order, because the close reads the flag this sets.
    /// </summary>
    public void ButtonSelected(string pressedButton)
    {
        ArgumentNullException.ThrowIfNull(pressedButton);

        requests.Add(new MappingRequest(
            MappingAction.UpdateButton, captureButtonValue, pressedButton, captureButtonIndex));

        quitButtonMapping = false;
        Altered = true;
        CloseCapture();
    }

    /// <summary>
    /// Closing the capture, by the Close button, by clicking outside it, or by a press having
    /// arrived. Quits the button mapping only in the first two cases - and puts the flag back
    /// either way, which is what makes it a one-shot rather than a mode.
    /// </summary>
    public void CloseCapture()
    {
        if (!CaptureOpen)
            return;

        CaptureOpen = false;

        if (quitButtonMapping)
            requests.Add(new MappingRequest(MappingAction.ButtonQuit));
        else
            quitButtonMapping = true;

        if (resetFocus)
            FocusOnClose = (captureMappingIndex, captureButtonIndex);
        else
            resetFocus = true;
    }

    /// <summary>Pressing Update: apply, then leave. Two calls and always both.</summary>
    public void Apply()
    {
        requests.Add(new MappingRequest(MappingAction.Apply));
        requests.Add(new MappingRequest(MappingAction.Quit));
    }

    /// <summary>
    /// The subsystem leaving mapping mode. Closes the screen - unless this screen is what stopped
    /// it, which is the guard that keeps a close from closing twice.
    /// </summary>
    public void MappingStopped()
    {
        inProgress = false;
        Raise(nameof(InProgress));

        if (!finalClosing)
            Closed = true;
    }

    /// <summary>
    /// The screen going away while mapping is still running: say so first, then quit both levels.
    /// The flag is set BEFORE the calls because those calls are what raise the signal the guard
    /// above reads.
    /// </summary>
    public void Destroyed()
    {
        if (!inProgress)
            return;

        finalClosing = true;
        requests.Add(new MappingRequest(MappingAction.ButtonQuit));
        requests.Add(new MappingRequest(MappingAction.Quit));
    }
}

/// <summary>
/// PP18: the mapping screen's rules where the Qt client states them.
/// </summary>
public static class ControllerMappingScreenSource
{
    /// <summary>The screen.</summary>
    public const string DialogQml = @"gui\src\qml\ControllerMappingDialog.qml";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(DialogQml);

    /// <summary>Whether the suppression flag is still a one-shot that puts itself back.</summary>
    public static bool TheQuitFlagIsStillAOneShot(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("property bool quitButtonMapping: true", StringComparison.Ordinal)
            && qml.Contains("quitButtonMapping = false;", StringComparison.Ordinal)
            && qml.Contains("quitButtonMapping = true;", StringComparison.Ordinal);
    }

    /// <summary>And whether the focus flag beside it has the same shape.</summary>
    public static bool TheFocusFlagHasTheSameShape(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("property bool resetFocus: true", StringComparison.Ordinal)
            && qml.Contains("resetFocus = true;", StringComparison.Ordinal);
    }

    /// <summary>Whether the screen still refuses to close itself twice.</summary>
    public static bool TheCloseGuardIsStillThere(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
            "if(!Chiaki.controllerMappingInProgress && !finalClosing)", StringComparison.Ordinal);
    }

    /// <summary>Whether Update still applies and quits, in that order.</summary>
    public static bool UpdateStillAppliesThenQuits(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);

        int apply = qml.IndexOf("Chiaki.controllerMappingApply();", StringComparison.Ordinal);
        int quit = qml.IndexOf("Chiaki.controllerMappingQuit();", StringComparison.Ordinal);

        return apply >= 0 && quit > apply;
    }

    /// <summary>Whether the Update button still needs something to have been altered.</summary>
    public static bool UpdateStillNeedsAnAlteration(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("buttonEnabled: Chiaki.controllerMappingAltered", StringComparison.Ordinal);
    }

    /// <summary>Whether the sticks and triggers are still behind an opt-in on this screen.</summary>
    public static bool TheAnalogOptInIsStillOnThisScreen(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
            "text: qsTr(\"Allow mapping analog sticks and L2/R2\")", StringComparison.Ordinal);
    }

    /// <summary>Whether a row can still carry a SECOND physical button.</summary>
    public static bool ARowStillCarriesTwoBindings(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("if(modelData.physicalButton.length > 1)", StringComparison.Ordinal);
    }
}

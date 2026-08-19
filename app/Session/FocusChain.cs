using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>Where a key press leaves the focus, and whether the control still sees the key.</summary>
/// <param name="Next">The index focus moves to, or the current one when it does not move.</param>
/// <param name="Handled">Whether the key was consumed. False means the control also acts on it.</param>
public readonly record struct FocusMove(int Next, bool Handled);

/// <summary>
/// PP12: how focus moves between controls, which is not what the task assumed.
///
/// The design filed for this block describes a directional focus engine - "which control the
/// stick moves to" - and the Qt client has nothing of the kind. All six controls in
/// gui/src/qml/controls handle exactly Up and Down, and both walk the TAB CHAIN:
/// nextItemInFocusChain(false) and nextItemInFocusChain(). Not one of them handles Left or Right.
///
/// That is a much smaller thing to port and a much better one to have found before porting it. A
/// spatial engine would have been a second navigation model, correct on its own terms and
/// different from the client people already use - a screen where the stick goes somewhere else.
///
/// Left and right are not missing; they are LEFT ALONE, so that a slider changes its value and a
/// combo box its selection. A port that claimed them for navigation would take that away.
///
/// What the chain does carry is three decisions.
/// </summary>
public sealed class FocusChain
{
    private readonly IReadOnlyList<FocusStop> stops;

    /// <param name="stops">The controls, in chain order - which is declaration order, not layout.</param>
    public FocusChain(IReadOnlyList<FocusStop> stops)
    {
        ArgumentNullException.ThrowIfNull(stops);
        this.stops = stops;
    }

    /// <summary>
    /// Where Up leaves focus.
    ///
    /// A stop marked first in the chain does not move and does not consume: the key falls through
    /// to whatever is above, which is how a list inside a screen hands navigation back out.
    /// </summary>
    public FocusMove Up(int current) => Move(current, forward: false);

    /// <summary>Where Down leaves focus. Symmetric, with the last stop as the boundary.</summary>
    public FocusMove Down(int current) => Move(current, forward: true);

    private FocusMove Move(int current, bool forward)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(current);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(current, stops.Count);

        FocusStop stop = stops[current];

        // The boundary is a PROPERTY of the control and not of its position. A stop can be marked
        // first while sitting in the middle of the list, which is how a screen splits one visual
        // column into two chains - so this asks the flag rather than comparing indices.
        bool atBoundary = forward ? stop.LastInChain : stop.FirstInChain;
        if (atBoundary)
            return new FocusMove(current, Handled: false);

        int next = forward ? current + 1 : current - 1;
        if (next < 0 || next >= stops.Count)
            return new FocusMove(current, Handled: !stop.SendOutput);

        // sendOutput is what lets a control move focus AND still act on the key. The Qt client
        // sets event.accepted only when it is false, so a stop with it on is one that navigates
        // and passes the press through as well.
        return new FocusMove(next, Handled: !stop.SendOutput);
    }
}

/// <summary>One control in the chain, with the two flags the QML carries and the pass-through.</summary>
/// <param name="FirstInChain">Up does not leave this control.</param>
/// <param name="LastInChain">Down does not leave this control.</param>
/// <param name="SendOutput">The key is passed on as well as acted upon.</param>
public readonly record struct FocusStop(
    bool FirstInChain = false, bool LastInChain = false, bool SendOutput = false);

/// <summary>
/// PP12: what the six QML controls actually bind, read out of them.
///
/// The port's control vocabulary has to answer the same keys as the Qt client's or the two
/// applications navigate differently, and that is not something a screenshot would show.
/// </summary>
public static partial class FocusChainSource
{
    /// <summary>The six controls, in the order the directory lists them.</summary>
    public static IReadOnlyList<string> Controls { get; } =
        ["Button", "CheckBox", "ComboBox", "RadioButton", "Slider", "TextField"];

    /// <summary>One control's QML, or null when this is not running out of a checkout.</summary>
    public static string? Locate(string control)
        => SanitizerSource.LocateRelative(Path.Combine("gui", "src", "qml", "controls", control + ".qml"));

    /// <summary>The Qt key names a control's Keys.onPressed switches on.</summary>
    public static IReadOnlySet<string> KeysHandled(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return KeyRegex().Matches(qml).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Whether the control walks the tab chain rather than any geometry.</summary>
    public static bool WalksTheTabChain(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("nextItemInFocusChain(false)", StringComparison.Ordinal)
            && qml.Contains("nextItemInFocusChain()", StringComparison.Ordinal);
    }

    /// <summary>Whether the two boundary flags still guard the two directions.</summary>
    public static bool GuardsBothEnds(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("!firstInFocusChain", StringComparison.Ordinal)
            && qml.Contains("!lastInFocusChain", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"Qt\.Key_([A-Za-z]+)")]
    private static partial Regex KeyRegex();
}

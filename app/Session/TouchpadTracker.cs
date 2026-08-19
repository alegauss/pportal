using System.Text.RegularExpressions;
using ChiakiNg.Native;

namespace ChiakiNg.Session;

/// <summary>
/// PP129: SDL's fingers mapped onto the console's two touch slots.
///
/// SDL identifies a touch by the PAIR (touchpad, finger) and reports it as three event types.
/// The console identifies one by an id it hands back when a touch starts, and it has room for
/// CHIAKI_CONTROLLER_TOUCHES_MAX of them - which is two. So something has to hold the mapping
/// between the two namings for the life of each touch, and that is all this is.
///
/// It is a small amount of code with four ways to be wrong, and all four are silent:
///
///   the key is the PAIR and not the finger. A DualSense has one touchpad, so a port keyed on
///   the finger alone works on every pad anyone testing it owns and collides on any device with
///   two - which is what the pair exists for;
///
///   a slot is only freed on UP. Lose that and the pool of two fills after two touches and the
///   touchpad stops responding for the rest of the session, with no error anywhere;
///
///   a MOTION for a finger that never went down is dropped rather than started. Treating it as a
///   new touch is how a finger that arrived during a reconnect consumes a slot it never releases;
///
///   and a DOWN past the maximum is refused BEFORE the touch is started, not after. Starting it
///   first would take an id the console has no room for and then leak it.
/// </summary>
public sealed class TouchpadTracker
{
    private readonly Dictionary<(int Touchpad, int Finger), byte> ids = [];

    /// <summary>The console's own limit, asked rather than assumed.</summary>
    public static int MaxTouches => ChiakiControllerState.TouchesMax;

    /// <summary>How many touches are currently held. Never more than <see cref="MaxTouches"/>.</summary>
    public int Count => ids.Count;

    /// <summary>
    /// A finger arriving. False when the console has no room, and nothing is started in that case
    /// - the refusal comes before the id is taken, so there is no id to leak.
    /// </summary>
    public bool Down(ChiakiControllerState state, int touchpad, int finger, ushort x, ushort y)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (ids.Count >= MaxTouches)
            return false;

        sbyte id = state.StartTouch(x, y);
        if (id < 0)
            return false;

        ids[(touchpad, finger)] = (byte)id;
        return true;
    }

    /// <summary>
    /// A finger arriving, with SDL's fractions and the pad they are a fraction OF.
    ///
    /// PP93: the extents are a parameter and not a constant, and they come from
    /// <see cref="TouchpadExtents"/> rather than from a pair of numbers here. The Qt client's SDL
    /// path scales by PS_TOUCHPAD_MAXX/MAXY - a pad that is neither console's - because this layer
    /// has no session to ask. The port's layer has none either; what it has instead is a caller
    /// obliged to say which pad, so the question cannot be answered by default.
    /// </summary>
    public bool Down(
        ChiakiControllerState state, int touchpad, int finger,
        float normX, float normY, TouchpadExtents extents)
    {
        (ushort x, ushort y) = extents.Scale(normX, normY);
        return Down(state, touchpad, finger, x, y);
    }

    /// <summary>
    /// A finger moving. False when this pair never went down - dropped rather than started,
    /// because a touch the console does not know about has no id to move.
    /// </summary>
    public bool Motion(ChiakiControllerState state, int touchpad, int finger, ushort x, ushort y)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!ids.TryGetValue((touchpad, finger), out byte id))
            return false;

        state.SetTouchPos(id, x, y);
        return true;
    }

    /// <summary>A finger moving, scaled the same way and by the same pad.</summary>
    public bool Motion(
        ChiakiControllerState state, int touchpad, int finger,
        float normX, float normY, TouchpadExtents extents)
    {
        (ushort x, ushort y) = extents.Scale(normX, normY);
        return Motion(state, touchpad, finger, x, y);
    }

    /// <summary>
    /// A finger leaving, which is the only thing that frees a slot. False when the pair is
    /// unknown, so a stray UP does not release somebody else's touch.
    /// </summary>
    public bool Up(ChiakiControllerState state, int touchpad, int finger)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!ids.Remove((touchpad, finger), out byte id))
            return false;

        state.StopTouch(id);
        return true;
    }

    /// <summary>
    /// Forgets every touch WITHOUT telling the console, for a controller that went away mid-touch.
    ///
    /// Separate from <see cref="Up"/> because the state it would be stopping touches in belongs to
    /// a pad that is no longer there; what needs clearing is this side's memory of the pairs.
    /// </summary>
    public void Forget() => ids.Clear();

    /// <summary>
    /// SDL reports a touch position as a fraction of the pad; the console wants it in its own
    /// units. The maxima are per-console and PP93 is where reading the wrong one cost something.
    /// </summary>
    public static ushort ToConsoleUnits(float normalised, int max)
        => (ushort)(normalised * max);
}

/// <summary>
/// PP129: the touchpad rules as the Qt client spells them, so the port's copy cannot drift.
/// </summary>
public static partial class TouchpadSource
{
    /// <summary>The Qt client's controller code.</summary>
    public const string RelativePath = @"gui\src\controllermanager.cpp";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Whether the touch is still keyed by the pair rather than by the finger alone.</summary>
    public static bool KeyedByTouchpadAndFinger(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Contains("qMakePair(event.touchpad, event.finger)", StringComparison.Ordinal);
    }

    /// <summary>Whether a DOWN past the maximum is still refused before the touch is started.</summary>
    public static bool RefusesBeforeStarting(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return RefusalOrderRegex().IsMatch(text);
    }

    /// <summary>Whether an UP still removes the key, which is what frees the slot.</summary>
    public static bool UpFreesTheSlot(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Contains("touch_ids.remove(key);", StringComparison.Ordinal);
    }

    [GeneratedRegex(
        @"if\(touch_ids\.size\(\) >= CHIAKI_CONTROLLER_TOUCHES_MAX\)\s*\r?\n\s*return false;\s*\r?\n\s*chiaki_id = chiaki_controller_state_start_touch")]
    private static partial Regex RefusalOrderRegex();
}

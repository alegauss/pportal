using ChiakiNg.Native;

namespace ChiakiNg.Session;

/// <summary>What one dpad-touch step did, which is what the Qt side drives its two timers off.</summary>
public enum DpadTouchAction
{
    /// <summary>No dpad direction was held. streamsession.cpp logs a warning and does nothing.</summary>
    None,

    /// <summary>There was no touch and one was started at the edge the direction points from.</summary>
    Started,

    /// <summary>An existing touch moved by the increment, or stayed where it already was.</summary>
    Moved,
}

/// <summary>
/// PP5: HandleDpadTouchEvent, which turns the dpad into a finger on the touchpad.
///
/// The Qt version is 90 lines of four near-identical blocks around two QTimers. Everything that is
/// not the timers is here: which direction wins, where a touch starts, how far it steps and where
/// it stops - and it is worth having on its own because three of those are decided by numbers that
/// look interchangeable and are not.
///
/// The direction order is a priority and not a set. Left is tested first and every block returns,
/// so left-and-up held together is a step left, and only the left bit is cleared from the pad
/// state - the up bit survives into whatever reads it next.
///
/// A touch starts AT the edge it comes from: pressing left with no touch down puts the finger at
/// x=0, already as far left as it goes. The second press cannot move it, and the same is true of
/// every direction. That is the Qt behaviour, and a port that started in the middle would give the
/// user a different gesture for the same press.
/// </summary>
public sealed class DpadTouch
{
    /// <summary>
    /// PP93: the connected console's own pad, which is what the mouse and touch paths already use.
    ///
    /// It used to be PS_TOUCHPAD_MAXX/MAXY from controllermanager.h - 1920x1079, the larger value
    /// of each axis and therefore neither pad, applied whichever console was connected. A
    /// DualShock 4 is 1920x942, so a dpad-touch "down" walked the finger a seventh of the height
    /// past the end of it.
    /// </summary>
    public ushort MaxX { get; }
    public ushort MaxY { get; }

    /// <param name="ps5">Which console is connected, which is the whole of what picks the pad.</param>
    public DpadTouch(bool ps5)
    {
        (float maxX, float maxY) = InputTranslation.TouchpadBounds(ps5);
        MaxX = (ushort)maxX;
        MaxY = (ushort)maxY;
    }

    /// <summary>
    /// settings/dpad_touch_increment, which is zero when the feature is off. Zero is not a special
    /// case in the arithmetic - a step of zero simply does not move - so nothing here tests it.
    /// </summary>
    public ushort Increment { get; set; }

    /// <summary>The libchiaki touch id in use, or -1 while no finger is down.</summary>
    public sbyte TouchId { get; private set; } = -1;

    /// <summary>Where the finger is, in touchpad coordinates.</summary>
    public (ushort X, ushort Y) Value { get; private set; }

    /// <summary>
    /// One step. Reads the dpad out of <paramref name="padState"/>, clears the bit it acted on,
    /// and starts or moves the finger in <paramref name="touchState"/>.
    /// </summary>
    public DpadTouchAction Handle(ChiakiControllerState padState, ChiakiControllerState touchState)
    {
        ArgumentNullException.ThrowIfNull(padState);
        ArgumentNullException.ThrowIfNull(touchState);

        // Order is the C++ file's order, and it is load-bearing: each block returns.
        // The halves are integer division, as the C++ macros are: 1079/2 is 539, not 539.5.
        ushort halfX = (ushort)(MaxX / 2);
        ushort halfY = (ushort)(MaxY / 2);

        if (Take(padState, ChiakiControllerButton.DpadLeft))
            return Step(touchState, start: ((ushort)0, halfY), stepped: (Down(Value.X), Value.Y));

        if (Take(padState, ChiakiControllerButton.DpadRight))
            return Step(touchState, start: (MaxX, halfY), stepped: (Up(Value.X, MaxX), Value.Y));

        if (Take(padState, ChiakiControllerButton.DpadDown))
            return Step(touchState, start: (halfX, MaxY), stepped: (Value.X, Up(Value.Y, MaxY)));

        if (Take(padState, ChiakiControllerButton.DpadUp))
            return Step(touchState, start: (halfX, (ushort)0), stepped: (Value.X, Down(Value.Y)));

        return DpadTouchAction.None;
    }

    /// <summary>Lifts the finger, which is what the Qt side's stop timer does when it fires.</summary>
    public void Stop(ChiakiControllerState touchState)
    {
        ArgumentNullException.ThrowIfNull(touchState);

        if (TouchId < 0)
            return;

        touchState.StopTouch((byte)TouchId);
        TouchId = -1;
    }

    private static bool Take(ChiakiControllerState padState, ChiakiControllerButton direction)
    {
        if ((padState.Buttons & direction) == 0)
            return false;

        // Cleared from the pad state, because a dpad bound to the touchpad must not also arrive at
        // the console as a dpad press.
        padState.Buttons &= ~direction;
        return true;
    }

    private DpadTouchAction Step(
        ChiakiControllerState touchState, (ushort X, ushort Y) start, (ushort X, ushort Y) stepped)
    {
        if (TouchId < 0)
        {
            Value = start;
            TouchId = touchState.StartTouch(start.X, start.Y);
            return DpadTouchAction.Started;
        }

        Value = stepped;
        touchState.SetTouchPos((byte)TouchId, Value.X, Value.Y);
        return DpadTouchAction.Moved;
    }

    /// <summary>A step toward zero that stops there rather than wrapping through it.</summary>
    private ushort Down(ushort value) => value < Increment ? (ushort)0 : (ushort)(value - Increment);

    /// <summary>A step toward the far edge that lands exactly on it.</summary>
    private ushort Up(ushort value, ushort max)
        => value > max - Increment ? max : (ushort)(value + Increment);
}

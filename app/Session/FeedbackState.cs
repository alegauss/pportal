using ChiakiNg.Native;

namespace ChiakiNg.Session;

/// <summary>
/// PP5: SendFeedbackState's decisions, without the controller list or the QTimer.
///
/// What the session sends upstream is the union of every input device plus the keyboard and the
/// touchpad, and between that union and the wire sit three pieces of state that are easy to write
/// and hard to write correctly: the input block, the shortcut chord that switches the dpad between
/// a dpad and a finger, and the gate that decides whether the dpad is being used as one.
///
/// Each is a latch rather than a condition, and a latch transcribed as a condition fires every
/// frame instead of once per press. At sixty frames a second that is the difference between a
/// setting toggling and a setting flickering.
/// </summary>
public sealed class FeedbackState
{
    /// <summary>The four dpad directions, as one mask.</summary>
    public const ChiakiControllerButton DpadMask =
        ChiakiControllerButton.DpadUp | ChiakiControllerButton.DpadDown
        | ChiakiControllerButton.DpadLeft | ChiakiControllerButton.DpadRight;

    private bool chordLatched;

    /// <summary>
    /// input_block. Two is "blocked until every button is released", which is what a screen sets
    /// when it hands the pad back to the stream: releasing the button that closed the menu must
    /// not arrive at the console as a press.
    /// </summary>
    public int InputBlock { get; set; }

    /// <summary>Whether the dpad is a dpad. False means it is driving a finger on the touchpad.</summary>
    public bool DpadRegular { get; set; } = true;

    /// <summary>The four shortcut bits, where zero means "not part of the chord".</summary>
    public uint[] Shortcuts { get; init; } = [0, 0, 0, 0];

    /// <summary>settings/dpad_touch_increment, zero when the feature is off.</summary>
    public ushort DpadTouchIncrement { get; set; }

    /// <summary>
    /// Applies the input block to the state about to be sent.
    ///
    /// The unblock is one-way and needs a completely empty button mask, so a user still holding
    /// anything stays blocked. Both the outgoing state AND the keyboard state are idled while
    /// blocked - the keyboard one because it is sticky: it holds what was pressed until a key-up
    /// arrives, and a key-up that happened while blocked never will.
    /// </summary>
    /// <returns>true when the state was blanked.</returns>
    public bool ApplyInputBlock(ChiakiControllerState state, ChiakiControllerState keyboardState)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(keyboardState);

        if (InputBlock == 0)
            return false;

        if (InputBlock == 2 && state.Buttons == ChiakiControllerButton.None)
        {
            InputBlock = 0;
            return false;
        }

        state.SetIdle();
        keyboardState.SetIdle();
        return true;
    }

    /// <summary>
    /// The chord that switches the dpad between a dpad and a finger.
    ///
    /// A zero shortcut is not part of the chord, and all four being zero means there is no chord
    /// at all - so the guard is "at least one is set AND every set one is held", which is not the
    /// same as "any is held" and not the same as "all four are held".
    ///
    /// The latch is what makes it a press rather than a state: the toggle happens on the frame the
    /// chord closes and not again until it opens. Without it the setting would flip sixty times a
    /// second for as long as the user held the buttons.
    /// </summary>
    /// <returns>true when this call toggled <see cref="DpadRegular"/>.</returns>
    public bool ApplyDpadShortcut(ChiakiControllerState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        uint buttons = (uint)state.Buttons;
        bool anySet = Shortcuts.Any(s => s != 0);
        bool allHeld = Shortcuts.All(s => s == 0 || (buttons & s) != 0);

        if (!anySet || !allHeld)
        {
            chordLatched = false;
            return false;
        }

        if (chordLatched)
            return false;

        chordLatched = true;
        DpadRegular = !DpadRegular;
        return true;
    }

    /// <summary>
    /// Whether this frame should drive the touchpad finger instead of sending a dpad press.
    ///
    /// Three conditions, and the first is the one that reads like a setting and acts like a
    /// switch: an increment of zero is how the feature being off is expressed, so it is checked
    /// here rather than anywhere a boolean would be.
    /// </summary>
    public bool ShouldDriveDpadTouch(ChiakiControllerState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return DpadTouchIncrement != 0 && !DpadRegular && (state.Buttons & DpadMask) != 0;
    }
}

using ChiakiNg.Native;

namespace ChiakiNg.Session;

/// <summary>
/// PP701: one SDL pad's events folded into the state a console is sent.
///
/// Block C reads "video and input path" and was empty when this was written, which was true of the
/// video half in exactly the way PP700 found: every piece existed and nothing joined them. The
/// state, the shim's setter and the session's lock were all built and tested - and
/// <c>SetControllerState</c> had one caller in the whole tree, the self-test. A window could show a
/// console's picture and not send it a single button press.
///
/// THE MAPPING IS CONTROLLERMANAGER.CPP'S, TRANSCRIBED. Which SDL button is which PlayStation
/// button is a decision about a wire format and not about taste - a port that guessed would put
/// circle where cross belongs and be discovered by somebody cancelling a purchase they meant to
/// confirm. The Qt client's <c>HandleButtonEvent</c> and <c>HandleAxisEvent</c> are the source and
/// <see cref="PadSource"/> holds this against them.
///
/// THREE THINGS IT DELIBERATELY DOES NOT DO. The client's MISC1 raises the microphone button and
/// the four paddles return false, both of which belong to features this port has not joined yet;
/// and the triggers set the PRESSURES only, never the L2 and R2 bits. Those bits exist for keyboard
/// bindings - streamsession.cpp sets l2_state to 0xff from a key - and a pad that set both would
/// report a trigger twice.
/// </summary>
public sealed class PadTranslation
{
    /// <summary>SDL_GameControllerButton, in the header's order.</summary>
    public static class PadButton
    {
        public const int A = 0;
        public const int B = 1;
        public const int X = 2;
        public const int Y = 3;
        public const int Back = 4;
        public const int Guide = 5;
        public const int Start = 6;
        public const int LeftStick = 7;
        public const int RightStick = 8;
        public const int LeftShoulder = 9;
        public const int RightShoulder = 10;
        public const int DpadUp = 11;
        public const int DpadDown = 12;
        public const int DpadLeft = 13;
        public const int DpadRight = 14;
        public const int Misc1 = 15;
        public const int Paddle1 = 16;
        public const int Paddle2 = 17;
        public const int Paddle3 = 18;
        public const int Paddle4 = 19;
        public const int Touchpad = 20;
    }

    /// <summary>
    /// The PlayStation button one SDL button is, or <see cref="ChiakiControllerButton.None"/> where
    /// it is not one this path sends.
    ///
    /// None covers three separate cases on purpose - a paddle, the microphone button, and anything
    /// SDL adds after this was written - because the caller does the same thing with all three.
    /// </summary>
    public static ChiakiControllerButton ButtonFor(int sdlButton) => sdlButton switch
    {
        PadButton.A => ChiakiControllerButton.Cross,
        PadButton.B => ChiakiControllerButton.Moon,
        PadButton.X => ChiakiControllerButton.Box,
        PadButton.Y => ChiakiControllerButton.Pyramid,
        PadButton.DpadLeft => ChiakiControllerButton.DpadLeft,
        PadButton.DpadRight => ChiakiControllerButton.DpadRight,
        PadButton.DpadUp => ChiakiControllerButton.DpadUp,
        PadButton.DpadDown => ChiakiControllerButton.DpadDown,
        PadButton.LeftShoulder => ChiakiControllerButton.L1,
        PadButton.RightShoulder => ChiakiControllerButton.R1,
        PadButton.LeftStick => ChiakiControllerButton.L3,
        PadButton.RightStick => ChiakiControllerButton.R3,
        PadButton.Start => ChiakiControllerButton.Options,
        PadButton.Back => ChiakiControllerButton.Share,
        PadButton.Guide => ChiakiControllerButton.Ps,
        PadButton.Touchpad => ChiakiControllerButton.Touchpad,
        _ => ChiakiControllerButton.None,
    };

    /// <summary>
    /// A trigger's SDL reading as the pressure the wire carries: <c>event.value >> 7</c>.
    ///
    /// SDL gives a trigger 0..32767 and the wire wants 0..255. The shift is the client's, and it is
    /// a shift rather than a divide by 129 - so full pull reads 255 and nothing else has to.
    /// </summary>
    public static byte Pressure(short axisValue) => axisValue > 0 ? (byte)(axisValue >> 7) : (byte)0;

    private ChiakiControllerButton buttons;
    private byte l2;
    private byte r2;
    private short leftX;
    private short leftY;
    private short rightX;
    private short rightY;

    /// <summary>The buttons currently held, as the mask the wire carries.</summary>
    public ChiakiControllerButton Buttons => buttons;

    /// <summary>
    /// Folds one SDL event in, and reports whether anything CHANGED.
    ///
    /// The return is the signal a caller sends on. libchiaki's feedback sender reads whatever state
    /// it was last given, so pushing every event would be pushing the same state repeatedly and
    /// pushing none would leave the console holding a stale one; a change is exactly the moment
    /// there is something new to say.
    /// </summary>
    public bool Offer(SdlEvent ev)
    {
        switch (ev.Type)
        {
            case Gamepads.EventType.ControllerButtonDown:
            case Gamepads.EventType.ControllerButtonUp:
                return Press(
                    ButtonFor(ev.Index), ev.Type == Gamepads.EventType.ControllerButtonDown);

            case Gamepads.EventType.ControllerAxisMotion:
                return Move(ev.Index, ev.AxisValue);

            default:
                return false;
        }
    }

    /// <summary>Writes what has been folded in so far into a state to push.</summary>
    public void WriteTo(ChiakiControllerState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.Buttons = buttons;
        state.Triggers = (l2, r2);
        state.Sticks = (leftX, leftY, rightX, rightY);
    }

    private bool Press(ChiakiControllerButton button, bool down)
    {
        if (button == ChiakiControllerButton.None)
            return false;

        ChiakiControllerButton next = down ? buttons | button : buttons & ~button;
        if (next == buttons)
            return false;

        buttons = next;
        return true;
    }

    private bool Move(int axis, short value)
    {
        switch (axis)
        {
            case Gamepads.ControllerAxis.TriggerLeft:
                return Changed(ref l2, Pressure(value));
            case Gamepads.ControllerAxis.TriggerRight:
                return Changed(ref r2, Pressure(value));
            case Gamepads.ControllerAxis.LeftX:
                return Changed(ref leftX, value);
            case Gamepads.ControllerAxis.LeftY:
                return Changed(ref leftY, value);
            case Gamepads.ControllerAxis.RightX:
                return Changed(ref rightX, value);
            case Gamepads.ControllerAxis.RightY:
                return Changed(ref rightY, value);
            default:
                return false;
        }
    }

    private static bool Changed<T>(ref T held, T now)
        where T : struct, IEquatable<T>
    {
        if (held.Equals(now))
            return false;

        held = now;
        return true;
    }
}

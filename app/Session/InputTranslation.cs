using ChiakiNg.Native;

namespace ChiakiNg.Session;

/// <summary>
/// ControllerButtonExt, the values settings.h assigns above libchiaki's own button bits so the two
/// can share one integer. They name the eight half-axes a key can be bound to, plus the four whole
/// axes and MISC1 that only the pad mapping uses.
/// </summary>
[Flags]
public enum ControllerButtonExt : uint
{
    AnalogStickLeftXUp = 1u << 18,
    AnalogStickLeftXDown = 1u << 19,
    AnalogStickLeftYUp = 1u << 20,
    AnalogStickLeftYDown = 1u << 21,
    AnalogStickRightXUp = 1u << 22,
    AnalogStickRightXDown = 1u << 23,
    AnalogStickRightYUp = 1u << 24,
    AnalogStickRightYDown = 1u << 25,
    AnalogStickLeftX = 1u << 26,
    AnalogStickLeftY = 1u << 27,
    AnalogStickRightX = 1u << 28,
    AnalogStickRightY = 1u << 29,
    Misc1 = 1u << 30,
}

/// <summary>
/// PP5: the keyboard and mouse halves of streamsession.cpp, with the QEvent taken off the front.
///
/// What is left once the event type is gone is a translation from "this binding went down" to a
/// change in a ChiakiControllerState, and it is worth having on its own because two of its rules
/// are invertible without anything reporting it: the vertical half-axes are NEGATIVE up while the
/// horizontal ones are POSITIVE up, and the triggers set a pressure rather than the bit of the
/// same name.
/// </summary>
public static class InputTranslation
{
    /// <summary>libchiaki's own analog-button bits, which are what a trigger binding carries.</summary>
    public const uint AnalogButtonL2 = 1u << 16;
    public const uint AnalogButtonR2 = 1u << 17;

    /// <summary>
    /// The touchpad the console expects coordinates in, and the two are not the same shape: a PS4
    /// pad is 1920x942 and a PS5 pad is 1919x1079. Off by one on the width and by a third on the
    /// height, so one pair used for both puts a touch in the wrong place on one of them.
    ///
    /// PP93: the numbers are <see cref="TouchpadExtents"/>'s and not this file's any more. They were
    /// here first, and leaving a copy behind is how the Qt client ended up with three answers.
    /// </summary>
    public static (float MaxX, float MaxY) TouchpadBounds(bool ps5)
    {
        TouchpadExtents pad = TouchpadExtents.For(ps5);
        return (pad.MaxX, pad.MaxY);
    }

    /// <summary>
    /// The mouse path: a scene coordinate clamped to the window, then scaled into touchpad space.
    ///
    /// PP91 is why the clamp is worth a sentence. Both touch paths in streamsession.cpp used to
    /// read <c>std::clamp(0.0, x, width)</c> - the value first and the bounds after, so the thing
    /// being clamped was the literal 0.0 and the upper bound never applied. A drag past the right
    /// edge told the console the finger was somewhere its touchpad does not reach. Fixed in both
    /// clients at once, and <see cref="SessionSource"/> keeps it fixed.
    /// </summary>
    public static (ushort X, ushort Y) MouseToTouchpad(
        double sceneX, double sceneY, double width, double height, bool ps5)
    {
        (float maxX, float maxY) = TouchpadBounds(ps5);

        float x = (float)Math.Clamp(sceneX, 0.0, width);
        float y = (float)Math.Clamp(sceneY, 0.0, height);

        // The ratio stays double and only the product narrows, which is what the C++ does: the
        // width is a qreal, so `PS_TOUCHPAD_MAX_X / width` is a double division and the float
        // appears at the assignment. Narrowing the ratio first instead loses 360 * 942/720 from
        // 471 to 470 - one pixel, on the axis, every time the pointer is in the lower half.
        return ((ushort)(float)(x * (maxX / width)), (ushort)(float)(y * (maxY / height)));
    }

    /// <summary>
    /// The touch path: normalise first, then scale. Clamped to 0..1, so a pointer outside the
    /// window lands on the edge of the touchpad rather than past it (PP91).
    /// </summary>
    public static float Normalize(double scene, double extent)
        => (float)Math.Clamp(scene / extent, 0.0, 1.0);

    /// <summary>A normalised point in touchpad coordinates.</summary>
    public static (ushort X, ushort Y) NormalizedToTouchpad(float normX, float normY, bool ps5)
    {
        (float maxX, float maxY) = TouchpadBounds(ps5);
        return ((ushort)(normX * maxX), (ushort)(normY * maxY));
    }

    /// <summary>
    /// Touching the outer 5% of any edge is a touchpad click. The rule is on the NORMALISED
    /// coordinate, so it fires for a value past 1.0 as well - which the clamp above lets through.
    /// </summary>
    public static bool IsEdgeTouch(float normX, float normY)
        => normX <= 0.05f || normX >= 0.95f || normY <= 0.05f || normY >= 0.95f;

    /// <summary>
    /// One binding going down or coming up, applied to a controller state.
    ///
    /// Two rules here are invertible with no symptom but a wrong stream. The vertical half-axes
    /// are negative up - Y_UP is -0x7fff - while the horizontal ones are positive up, which is
    /// the screen's coordinate sense on one axis and not the other. And a trigger binding sets
    /// l2_state or r2_state to 0xff rather than setting the analog-button bit: the bit is what the
    /// MAPPING carries, the pressure is what the console reads.
    /// </summary>
    public static void ApplyBinding(ChiakiControllerState state, uint binding, bool pressed)
    {
        ArgumentNullException.ThrowIfNull(state);

        switch (binding)
        {
            case AnalogButtonL2:
                state.Triggers = (pressed ? (byte)0xff : (byte)0, state.Triggers.R2);
                break;
            case AnalogButtonR2:
                state.Triggers = (state.Triggers.L2, pressed ? (byte)0xff : (byte)0);
                break;
            case (uint)ControllerButtonExt.AnalogStickRightYUp:
                SetStick(state, rightY: pressed ? (short)-0x7fff : (short)0);
                break;
            case (uint)ControllerButtonExt.AnalogStickRightYDown:
                SetStick(state, rightY: pressed ? (short)0x7fff : (short)0);
                break;
            case (uint)ControllerButtonExt.AnalogStickRightXUp:
                SetStick(state, rightX: pressed ? (short)0x7fff : (short)0);
                break;
            case (uint)ControllerButtonExt.AnalogStickRightXDown:
                SetStick(state, rightX: pressed ? (short)-0x7fff : (short)0);
                break;
            case (uint)ControllerButtonExt.AnalogStickLeftYUp:
                SetStick(state, leftY: pressed ? (short)-0x7fff : (short)0);
                break;
            case (uint)ControllerButtonExt.AnalogStickLeftYDown:
                SetStick(state, leftY: pressed ? (short)0x7fff : (short)0);
                break;
            case (uint)ControllerButtonExt.AnalogStickLeftXUp:
                SetStick(state, leftX: pressed ? (short)0x7fff : (short)0);
                break;
            case (uint)ControllerButtonExt.AnalogStickLeftXDown:
                SetStick(state, leftX: pressed ? (short)-0x7fff : (short)0);
                break;
            default:
                // Everything else is a bit in the mask, including the touchpad and PS buttons.
                state.Buttons = pressed
                    ? state.Buttons | (ChiakiControllerButton)binding
                    : state.Buttons & ~(ChiakiControllerButton)binding;
                break;
        }
    }

    private static void SetStick(
        ChiakiControllerState state, short? leftX = null, short? leftY = null,
        short? rightX = null, short? rightY = null)
    {
        (short lx, short ly, short rx, short ry) = state.Sticks;
        state.Sticks = (leftX ?? lx, leftY ?? ly, rightX ?? rx, rightY ?? ry);
    }
}

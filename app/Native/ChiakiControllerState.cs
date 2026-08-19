using System.Runtime.InteropServices;

namespace ChiakiNg.Native;

/// <summary>
/// ChiakiControllerButton, plus the two analog-button bits that sit above it. One bitmask, and the
/// values are the wire's rather than this side's - a mapping screen writes them straight through.
/// </summary>
[Flags]
public enum ChiakiControllerButton : uint
{
    None = 0,
    Cross = 1u << 0,
    Moon = 1u << 1,
    Box = 1u << 2,
    Pyramid = 1u << 3,
    DpadLeft = 1u << 4,
    DpadRight = 1u << 5,
    DpadUp = 1u << 6,
    DpadDown = 1u << 7,
    L1 = 1u << 8,
    R1 = 1u << 9,
    L3 = 1u << 10,
    R3 = 1u << 11,
    Options = 1u << 12,
    Share = 1u << 13,
    Touchpad = 1u << 14,
    Ps = 1u << 15,

    /// <summary>L2 and R2 as bits. The pressures are separate; these must not overlap the above.</summary>
    L2 = 1u << 16,
    R2 = 1u << 17,
}

/// <summary>One touch slot. <see cref="Id"/> is -1 for a finger that is up.</summary>
public readonly record struct ChiakiControllerTouch(ushort X, ushort Y, int Id);

/// <summary>
/// PP4: the controller state, built in C and pushed by handle.
///
/// It is twenty-one scalars, a two-element touch array and ten floats of motion, and the session
/// sends it upstream sixty times a second. Marshalling that per frame would make it the seam's
/// hottest path and its most detailed layout promise at once - the worst pairing available,
/// because an offset that is wrong by two bytes surfaces as a stick drift nobody traces back to a
/// struct definition.
///
/// The touch ids are libchiaki's, not this side's: <see cref="StartTouch"/> allocates a slot and
/// answers -1 when both are taken. A port that numbered its own fingers would eventually disagree
/// with the console about which one left.
/// </summary>
public sealed class ChiakiControllerState : IDisposable
{
    /// <summary>Both slots, which is CHIAKI_CONTROLLER_TOUCHES_MAX.</summary>
    public const int TouchesMax = 2;

    private IntPtr _handle;

    /// <summary>A state set to idle, which is what a pad being held still reports.</summary>
    public ChiakiControllerState()
    {
        _handle = ControllerStateCreate();
        if (_handle == IntPtr.Zero)
            throw new OutOfMemoryException("chiaki_shim_controller_state_create returned null.");
    }

    internal IntPtr Handle
        => _handle != IntPtr.Zero
            ? _handle
            : throw new ObjectDisposedException(nameof(ChiakiControllerState));

    /// <summary>chiaki_controller_state_set_idle.</summary>
    public void SetIdle() => ControllerStateSetIdle(Handle);

    public ChiakiControllerButton Buttons
    {
        get => (ChiakiControllerButton)ControllerStateButtons(Handle);
        set => ControllerStateSetButtons(Handle, (uint)value);
    }

    /// <summary>L2 and R2 pressures, which are not the bits of the same name in the mask.</summary>
    public (byte L2, byte R2) Triggers
    {
        get
        {
            ControllerStateTriggers(Handle, out byte l2, out byte r2);
            return (l2, r2);
        }
        set => ControllerStateSetTriggers(Handle, value.L2, value.R2);
    }

    public (short LeftX, short LeftY, short RightX, short RightY) Sticks
    {
        get
        {
            ControllerStateSticks(Handle, out short lx, out short ly, out short rx, out short ry);
            return (lx, ly, rx, ry);
        }
        set => ControllerStateSetSticks(Handle, value.LeftX, value.LeftY, value.RightX, value.RightY);
    }

    /// <summary>Gyro, accelerometer and orientation, in that order.</summary>
    /// <summary>
    /// PP130: the orientation quaternion the state carries, which is what the console is sent.
    ///
    /// A getter and not only a setter, because the orientation is written by the fusion filter
    /// rather than by the caller - so without this there is no way to see that it arrived.
    /// </summary>
    public (float X, float Y, float Z, float W) Orientation()
    {
        var orient = new float[4];
        if (!ControllerStateOrient(Handle, orient))
            throw new InvalidOperationException("chiaki_shim_controller_state_orient failed.");

        return (orient[0], orient[1], orient[2], orient[3]);
    }

    public void SetMotion(
        float gyroX, float gyroY, float gyroZ,
        float accelX, float accelY, float accelZ,
        float orientX, float orientY, float orientZ, float orientW)
        => ControllerStateSetMotion(Handle, gyroX, gyroY, gyroZ, accelX, accelY, accelZ,
            orientX, orientY, orientZ, orientW);

    /// <summary>The library's own allocation: a non-negative id, or -1 when both slots are taken.</summary>
    public sbyte StartTouch(ushort x, ushort y) => ControllerStateStartTouch(Handle, x, y);

    public void StopTouch(byte id) => ControllerStateStopTouch(Handle, id);

    public void SetTouchPos(byte id, ushort x, ushort y) => ControllerStateSetTouchPos(Handle, id, x, y);

    /// <summary>One slot, read back. Throws for a slot outside <see cref="TouchesMax"/>.</summary>
    public ChiakiControllerTouch Touch(int slot)
    {
        if (!ControllerStateTouch(Handle, slot, out ushort x, out ushort y, out int id))
            throw new ArgumentOutOfRangeException(nameof(slot), slot, $"there are {TouchesMax} touch slots.");
        return new ChiakiControllerTouch(x, y, id);
    }

    /// <summary>
    /// chiaki_controller_state_equals, which is the only comparison this seam uses. Written this
    /// way on purpose: a field-by-field comparison here would agree with a transcription this side
    /// also made, and then both would be wrong together.
    /// </summary>
    public bool Matches(ChiakiControllerState other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return ControllerStateEquals(Handle, other.Handle);
    }

    /// <summary>
    /// chiaki_controller_state_or, folding <paramref name="other"/> into this state.
    ///
    /// Not a union in three places, which is why it is the library's and not a loop here: the
    /// sticks take the larger MAGNITUDE and keep its sign, a touch slot prefers whichever side has
    /// a finger in it, and the motion axes are taken whole from the first state that has any
    /// rather than mixed - gyro and accelerometer readings from two devices average into an
    /// orientation that belongs to neither.
    /// </summary>
    public void Or(ChiakiControllerState other)
    {
        ArgumentNullException.ThrowIfNull(other);
        ControllerStateOr(Handle, Handle, other.Handle);
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;

        ControllerStateFree(_handle);
        _handle = IntPtr.Zero;
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_controller_state_create",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ControllerStateCreate();

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_controller_state_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ControllerStateFree(IntPtr state);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_controller_state_set_idle",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ControllerStateSetIdle(IntPtr state);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_controller_state_set_buttons",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ControllerStateSetButtons(IntPtr state, uint buttons);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_controller_state_buttons",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern uint ControllerStateButtons(IntPtr state);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_controller_state_set_triggers",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ControllerStateSetTriggers(IntPtr state, byte l2, byte r2);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_controller_state_triggers",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ControllerStateTriggers(IntPtr state, out byte l2, out byte r2);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_controller_state_set_sticks",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ControllerStateSetSticks(
        IntPtr state, short leftX, short leftY, short rightX, short rightY);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_controller_state_sticks",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ControllerStateSticks(
        IntPtr state, out short leftX, out short leftY, out short rightX, out short rightY);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_controller_state_orient",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool ControllerStateOrient(IntPtr state, float[] orient);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_controller_state_set_motion",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ControllerStateSetMotion(
        IntPtr state,
        float gyroX, float gyroY, float gyroZ,
        float accelX, float accelY, float accelZ,
        float orientX, float orientY, float orientZ, float orientW);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_controller_state_start_touch",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern sbyte ControllerStateStartTouch(IntPtr state, ushort x, ushort y);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_controller_state_stop_touch",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ControllerStateStopTouch(IntPtr state, byte id);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_controller_state_set_touch_pos",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ControllerStateSetTouchPos(IntPtr state, byte id, ushort x, ushort y);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_controller_state_touch",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool ControllerStateTouch(
        IntPtr state, int slot, out ushort x, out ushort y, out int id);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_controller_state_equals",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool ControllerStateEquals(IntPtr a, IntPtr b);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_controller_state_or",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ControllerStateOr(IntPtr outState, IntPtr a, IntPtr b);
}

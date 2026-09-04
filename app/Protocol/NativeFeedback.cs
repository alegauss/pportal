using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP676: feedback.c's serialisers through the shim, which is what the managed ones are held to.
///
/// Pure calls: no session, no socket, no key. What they format is a controller's state and a
/// history of its events, so the whole oracle runs in a unit test - which is why PP676's criterion
/// can be met without a console even though the sends that carry these payloads cannot.
/// </summary>
public static class NativeFeedback
{
    /// <summary>The size the C says a state of this version is.</summary>
    public static int StateSize(bool v12) => FeedbackStateSize(v12);

    /// <summary>chiaki_feedback_state_format_v9 or _v12, over the same state the managed side takes.</summary>
    public static byte[] FormatState(bool v12, FeedbackMotion motion)
    {
        float[] fields =
        [
            motion.GyroX, motion.GyroY, motion.GyroZ,
            motion.AccelX, motion.AccelY, motion.AccelZ,
            motion.OrientX, motion.OrientY, motion.OrientZ, motion.OrientW,
        ];

        short[] sticks = [motion.LeftX, motion.LeftY, motion.RightX, motion.RightY];

        byte[] buf = new byte[StateSize(v12)];
        FeedbackStateFormat(buf, buf.Length, v12, fields, sticks);
        return buf;
    }

    /// <summary>chiaki_feedback_history_event_set_button. Null where the C refuses the button.</summary>
    public static byte[]? ButtonEvent(ChiakiControllerButton button, byte state)
    {
        byte[] buf = new byte[FeedbackPayload.HistoryEventSizeMax];

        var error = (ChiakiError)FeedbackHistoryButton((ulong)button, state, buf, out int written);
        return error == ChiakiError.Success ? buf[..written] : null;
    }

    /// <summary>chiaki_feedback_history_event_set_touchpad, which never refuses.</summary>
    public static byte[] TouchpadEvent(bool down, byte pointerId, ushort x, ushort y)
    {
        byte[] buf = new byte[FeedbackPayload.HistoryEventSizeMax];

        FeedbackHistoryTouchpad(down, pointerId, x, y, buf, out int written);
        return buf[..written];
    }

    /// <summary>
    /// The ring buffer driven end to end: init, push each in order, format.
    /// </summary>
    /// <returns>The bytes, or null where the C answered anything but success.</returns>
    public static byte[]? FormatHistory(int size, IReadOnlyList<byte[]> events, int capacity)
    {
        ArgumentNullException.ThrowIfNull(events);

        byte[] flat = [.. events.SelectMany(one => one)];
        int[] lens = [.. events.Select(one => one.Length)];
        byte[] outBuf = new byte[capacity];

        int written = capacity;
        var error = (ChiakiError)FeedbackHistoryFormat(
            size, flat, lens, events.Count, outBuf, ref written);

        return error == ChiakiError.Success ? outBuf[..written] : null;
    }

    /// <summary>Whether this build carries the wrappers at all.</summary>
    public static bool IsAvailable()
    {
        try
        {
            return FeedbackStateSize(false) == FeedbackPayload.StateSizeV9;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_feedback_state_size",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I4)]
    private static extern int FeedbackStateSize([MarshalAs(UnmanagedType.I1)] bool v12);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_feedback_state_format",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void FeedbackStateFormat(
        byte[] buf, int bufSize, [MarshalAs(UnmanagedType.I1)] bool v12, float[] motion, short[] sticks);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_feedback_history_button",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int FeedbackHistoryButton(
        ulong button, byte state, byte[] outBuf, out int outLen);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_feedback_history_touchpad",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void FeedbackHistoryTouchpad(
        [MarshalAs(UnmanagedType.I1)] bool down, byte pointerId, ushort x, ushort y,
        byte[] outBuf, out int outLen);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_feedback_history_format",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int FeedbackHistoryFormat(
        int size, byte[] events, int[] lens, int count, byte[] outBuf, ref int outSize);
}

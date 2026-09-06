using System.Buffers.Binary;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// A controller's motion and sticks, as feedback.c's ChiakiFeedbackState carries them.
/// </summary>
/// <param name="GyroX">Radians per second, clamped into ±30 by the format rather than by this.</param>
/// <param name="GyroY">The same.</param>
/// <param name="GyroZ">The same.</param>
/// <param name="AccelX">G, over ±5.</param>
/// <param name="AccelY">The same.</param>
/// <param name="AccelZ">The same.</param>
/// <param name="OrientX">The orientation quaternion, compressed to thirty-two bits by the format.</param>
/// <param name="OrientY">The same.</param>
/// <param name="OrientZ">The same.</param>
/// <param name="OrientW">The same.</param>
/// <param name="LeftX">Signed, sent big-endian.</param>
/// <param name="LeftY">The same.</param>
/// <param name="RightX">The same.</param>
/// <param name="RightY">The same.</param>
public readonly record struct FeedbackMotion(
    float GyroX, float GyroY, float GyroZ,
    float AccelX, float AccelY, float AccelZ,
    float OrientX, float OrientY, float OrientZ, float OrientW,
    short LeftX, short LeftY, short RightX, short RightY)
{
    /// <summary>
    /// PP756: the fourteen read off a live state, which nothing could do until the gyro and the
    /// accelerometer had a reader.
    ///
    /// Three calls and not one, because the C keeps them in three groups and the shim wraps each -
    /// six floats, four, and the four stick axes that are not floats at all.
    /// </summary>
    public static FeedbackMotion From(ChiakiControllerState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        (float gyroX, float gyroY, float gyroZ, float accelX, float accelY, float accelZ) = state.Motion();
        (float orientX, float orientY, float orientZ, float orientW) = state.Orientation();
        (short leftX, short leftY, short rightX, short rightY) = state.Sticks;

        return new FeedbackMotion(
            gyroX, gyroY, gyroZ,
            accelX, accelY, accelZ,
            orientX, orientY, orientZ, orientW,
            leftX, leftY, rightX, rightY);
    }
}

/// <summary>
/// PP676: feedback.c's two serialisers and its history events, in managed code.
///
/// NOT <c>ChiakiNg.Session.FeedbackState</c>, which is PP5's model of the GUI's input decisions -
/// the input block, the shortcut chord, the dpad gate - and has nothing to do with these bytes. The
/// collision is in the C's own naming and is why this is called a payload.
///
/// THE QUATERNION IS THE PART A PORT GETS QUIETLY WRONG. Ten floats go in and four bytes come out:
/// the largest component is dropped and its INDEX and SIGN are carried instead, and the other three
/// are clamped to ±1/√2, shifted positive, scaled to nine bits and packed three bits up. A port that
/// picked the largest by value rather than by magnitude, or that packed the components in their own
/// order rather than skipping the largest, produces a number of the right size that turns a
/// controller a different way. Nothing about that fails - it aims wrongly.
///
/// AND THE HISTORY RING PUSHES BACKWARDS. chiaki_feedback_history_buffer_push moves `begin` to
/// <c>(begin + size - 1) % size</c>, so the newest event is the first one formatted. A port that
/// appended would send a console its button history in reverse order, which reads as input lag
/// rather than as a bug.
///
/// SIX BUTTONS CARRY THEIR STATE IN THE SECOND BYTE AND THE REST IN A THIRD. L3, R3, options,
/// share, touchpad and PS answer with one of two codes and a two-byte event; every other button
/// writes its code and then the state, three bytes. The C returns early for the six, which is the
/// distinction rather than an optimisation.
/// </summary>
public static class FeedbackPayload
{
    /// <summary>GYRO_MIN.</summary>
    public const float GyroMin = -30.0f;

    /// <summary>GYRO_MAX.</summary>
    public const float GyroMax = 30.0f;

    /// <summary>ACCEL_MIN.</summary>
    public const float AccelMin = -5.0f;

    /// <summary>ACCEL_MAX.</summary>
    public const float AccelMax = 5.0f;

    /// <summary>CHIAKI_FEEDBACK_STATE_BUF_SIZE_V9.</summary>
    public const int StateSizeV9 = 0x19;

    /// <summary>CHIAKI_FEEDBACK_STATE_BUF_SIZE_V12.</summary>
    public const int StateSizeV12 = 0x1c;

    /// <summary>CHIAKI_HISTORY_EVENT_SIZE_MAX.</summary>
    public const int HistoryEventSizeMax = 5;

    /// <summary>How many bytes a state of this version takes.</summary>
    public static int StateSize(bool v12) => v12 ? StateSizeV12 : StateSizeV9;

    /// <summary>
    /// compress_quat: the largest component's index and sign, then the other three at nine bits.
    ///
    /// LARGEST BY MAGNITUDE, which is what fabs is doing and what a port comparing values would
    /// miss on a quaternion whose largest component is negative. The sign it drops is carried in
    /// bit zero and the index in bits one and two.
    /// </summary>
    public static uint CompressQuaternion(float x, float y, float z, float w)
    {
        float[] q = [x, y, z, w];

        int largest = 0;
        for (int i = 1; i < 4; i++)
        {
            if (Math.Abs(q[i]) > Math.Abs(q[largest]))
                largest = i;
        }

        uint r = (uint)((q[largest] < 0.0 ? 1 : 0) | (largest << 1));

        // The C's M_SQRT1_2, as a double: the scale below is computed in double and the cast to
        // float happens once, at the end, exactly where the C puts it.
        const double Sqrt1_2 = 0.70710678118654752440;

        for (int i = 0; i < 3; i++)
        {
            // The largest component is SKIPPED rather than zeroed, so the three that are sent are
            // the other three in their own order.
            int qi = i < largest ? i : i + 1;

            float v = q[qi];
            if (v < -Sqrt1_2)
                v = (float)-Sqrt1_2;
            if (v > Sqrt1_2)
                v = (float)Sqrt1_2;

            v += (float)Sqrt1_2;
            v *= (float)(0x1ff / (2.0f * Sqrt1_2));

            r |= (uint)v << (3 + (i * 9));
        }

        return r;
    }

    /// <summary>
    /// chiaki_feedback_state_format_v9, and _v12 with three bytes after it.
    /// </summary>
    /// <param name="buf">At least <see cref="StateSize"/> for this version.</param>
    /// <param name="v12">Which of the two the takion negotiated - version 9 or below is v9.</param>
    /// <param name="motion">The controller's state.</param>
    public static void FormatState(Span<byte> buf, bool v12, FeedbackMotion motion)
    {
        int size = StateSize(v12);
        if (buf.Length < size)
            throw new ArgumentException($"a v{(v12 ? 12 : 9)} state is {size} bytes", nameof(buf));

        buf[0x0] = 0xa0;

        // LITTLE-endian, written a byte at a time by the C - and the sticks below are big-endian
        // through htons. Both in one payload, which is why neither is written with a helper that
        // would make them look alike.
        WriteScaled(buf, 0x1, motion.GyroX, GyroMin, GyroMax);
        WriteScaled(buf, 0x3, motion.GyroY, GyroMin, GyroMax);
        WriteScaled(buf, 0x5, motion.GyroZ, GyroMin, GyroMax);
        WriteScaled(buf, 0x7, motion.AccelX, AccelMin, AccelMax);
        WriteScaled(buf, 0x9, motion.AccelY, AccelMin, AccelMax);
        WriteScaled(buf, 0xb, motion.AccelZ, AccelMin, AccelMax);

        uint qc = CompressQuaternion(motion.OrientX, motion.OrientY, motion.OrientZ, motion.OrientW);
        buf[0xd] = (byte)qc;
        buf[0xe] = (byte)(qc >> 0x8);
        buf[0xf] = (byte)(qc >> 0x10);
        buf[0x10] = (byte)(qc >> 0x18);

        BinaryPrimitives.WriteUInt16BigEndian(buf[0x11..], (ushort)motion.LeftX);
        BinaryPrimitives.WriteUInt16BigEndian(buf[0x13..], (ushort)motion.LeftY);
        BinaryPrimitives.WriteUInt16BigEndian(buf[0x15..], (ushort)motion.RightX);
        BinaryPrimitives.WriteUInt16BigEndian(buf[0x17..], (ushort)motion.RightY);

        if (!v12)
            return;

        buf[0x19] = 0x0;
        buf[0x1a] = 0x0;
        buf[0x1b] = 0x1;
    }

    /// <summary>
    /// One axis, scaled across its range into sixteen bits and written low byte first.
    /// </summary>
    /// <remarks>
    /// The cast is the C's and it TRUNCATES rather than rounding, and it wraps rather than
    /// saturating: a value outside the range produces a uint16 the C would also produce, because
    /// the arithmetic is float and the cast is the same one. Clamping here would be a correction
    /// the console does not expect.
    /// </remarks>
    private static void WriteScaled(Span<byte> buf, int at, float value, float min, float max)
    {
        ushort v = (ushort)(0xffff * (value - min) / (max - min));
        buf[at] = (byte)v;
        buf[at + 1] = (byte)(v >> 8);
    }

    /// <summary>The six buttons whose state is in the code rather than in a third byte.</summary>
    /// <remarks>
    /// Each answers with one of two codes - pressed and not - and the event is two bytes. The C
    /// returns early for these six, which is what makes them a set rather than a shortcut.
    /// </remarks>
    private static readonly Dictionary<ChiakiControllerButton, (byte Up, byte Down)> TwoByteButtons =
        new()
        {
            [ChiakiControllerButton.L3] = (0x8f, 0xaf),
            [ChiakiControllerButton.R3] = (0x90, 0xb0),
            [ChiakiControllerButton.Options] = (0x8c, 0xac),
            [ChiakiControllerButton.Share] = (0x8d, 0xad),
            [ChiakiControllerButton.Touchpad] = (0x91, 0xb1),
            [ChiakiControllerButton.Ps] = (0x8e, 0xae),
        };

    /// <summary>The rest, whose code is fixed and whose state follows it.</summary>
    private static readonly Dictionary<ChiakiControllerButton, byte> ThreeByteButtons =
        new()
        {
            [ChiakiControllerButton.Cross] = 0x88,
            [ChiakiControllerButton.Moon] = 0x89,
            [ChiakiControllerButton.Box] = 0x8a,
            [ChiakiControllerButton.Pyramid] = 0x8b,
            [ChiakiControllerButton.DpadLeft] = 0x82,
            [ChiakiControllerButton.DpadRight] = 0x83,
            [ChiakiControllerButton.DpadUp] = 0x80,
            [ChiakiControllerButton.DpadDown] = 0x81,
            [ChiakiControllerButton.L1] = 0x84,
            [ChiakiControllerButton.R1] = 0x85,
            [ChiakiControllerButton.L2] = 0x86,
            [ChiakiControllerButton.R2] = 0x87,
        };

    /// <summary>
    /// chiaki_feedback_history_event_set_button.
    /// </summary>
    /// <param name="buf">At least <see cref="HistoryEventSizeMax"/>.</param>
    /// <param name="button">A ChiakiControllerButton or one of the two analog buttons.</param>
    /// <param name="state">0 for released, 0xff for pressed, between for an analog trigger.</param>
    /// <param name="written">How many bytes the event is, or zero where it is not one.</param>
    /// <returns>InvalidData for a button the C has no code for, which is not an error here either.</returns>
    public static ChiakiError ButtonEvent(
        Span<byte> buf, ChiakiControllerButton button, byte state, out int written)
    {
        if (buf.Length < HistoryEventSizeMax)
            throw new ArgumentException($"an event is up to {HistoryEventSizeMax} bytes", nameof(buf));

        written = 0;
        buf[0] = 0x80;

        if (TwoByteButtons.TryGetValue(button, out (byte Up, byte Down) codes))
        {
            // The STATE picks the code, and any non-zero is pressed - the C's `state ?`, which is a
            // truth test and not a comparison against 0xff.
            buf[1] = state != 0 ? codes.Down : codes.Up;
            written = 2;
            return ChiakiError.Success;
        }

        if (!ThreeByteButtons.TryGetValue(button, out byte code))
            return ChiakiError.InvalidData;

        buf[1] = code;
        buf[2] = state;
        written = 3;
        return ChiakiError.Success;
    }

    /// <summary>
    /// chiaki_feedback_history_event_set_touchpad, which never fails.
    /// </summary>
    /// <param name="buf">At least <see cref="HistoryEventSizeMax"/>.</param>
    /// <param name="down">Whether the finger is down, which picks the leading byte.</param>
    /// <param name="pointerId">Masked to seven bits by the format.</param>
    /// <param name="x">0 to 1920, packed into twelve bits.</param>
    /// <param name="y">0 to 942, packed into twelve bits sharing a byte with x.</param>
    public static int TouchpadEvent(Span<byte> buf, bool down, byte pointerId, ushort x, ushort y)
    {
        if (buf.Length < HistoryEventSizeMax)
            throw new ArgumentException($"an event is up to {HistoryEventSizeMax} bytes", nameof(buf));

        buf[0] = down ? (byte)0xd0 : (byte)0xc0;
        buf[1] = (byte)(pointerId & 0x7f);
        buf[2] = (byte)(x >> 4);

        // The two coordinates SHARE this byte: x's low nibble above, y's high nibble below.
        buf[3] = (byte)(((x & 0xf) << 4) | (byte)(y >> 8));
        buf[4] = (byte)y;

        return HistoryEventSizeMax;
    }

    /// <summary>
    /// The ring buffer, pushed and formatted the way the C does it.
    /// </summary>
    /// <param name="size">The ring's capacity. Pushes past it overwrite the oldest.</param>
    /// <param name="events">Each event's bytes, in the order they are pushed.</param>
    /// <param name="buf">Where the formatted history goes.</param>
    /// <param name="written">How many bytes were written.</param>
    /// <returns>BufTooSmall where the events do not fit, exactly where the C stops.</returns>
    /// <remarks>
    /// NEWEST FIRST, which is what the backwards push produces. The C moves begin to
    /// <c>(begin + size - 1) % size</c> and writes there, so a walk from begin reads the most
    /// recent event first - and a port that appended would format the same bytes in the opposite
    /// order.
    /// </remarks>
    public static ChiakiError FormatHistory(
        int size, IReadOnlyList<byte[]> events, Span<byte> buf, out int written)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        ArgumentNullException.ThrowIfNull(events);

        var ring = new byte[size][];
        int begin = 0;
        int len = 0;

        foreach (byte[] one in events)
        {
            begin = (begin + size - 1) % size;
            len = Math.Min(len + 1, size);
            ring[begin] = one;
        }

        written = 0;
        for (int i = 0; i < len; i++)
        {
            byte[] one = ring[(begin + i) % size] ?? [];

            if (written + one.Length > buf.Length)
                return ChiakiError.BufTooSmall;

            one.CopyTo(buf[written..]);
            written += one.Length;
        }

        return ChiakiError.Success;
    }
}

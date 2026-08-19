using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ChiakiNg.Native;

namespace ChiakiNg.Session;

/// <summary>A quaternion, as the console is told the pad is oriented.</summary>
public readonly record struct Orientation(float X, float Y, float Z, float W);

/// <summary>
/// PP130: the pad's sensors, fused into the orientation the console is sent.
///
/// A DualSense reports acceleration and angular velocity; the console expects a quaternion beside
/// them. The fusion is carried across the seam rather than ported, because it is a filter with
/// state - each update depends on the last and on the time between them, so a managed rewrite
/// would be a second filter drifting differently. Drift is a picture that slowly tilts, which
/// nobody reports as a bug in a client.
///
/// What is NOT carried is the arithmetic on the way in, because that is where the port can be
/// wrong on its own:
///
///   acceleration is divided by standard gravity and angular velocity is not. SDL reports the
///   first in m/s² and the console wants g, and the two sensors arrive through one event type -
///   so a rewrite that normalised both, or neither, is one line and no error;
///
///   and the timestamp is MICROseconds while SDL's is milliseconds. Get that wrong and the filter
///   believes a thousand times more or less time passed than did, which is an orientation that
///   lags or snaps rather than one that is wrong.
/// </summary>
public sealed class MotionTracker : IDisposable
{
    /// <summary>SDL_STANDARD_GRAVITY. The divisor, and only for acceleration.</summary>
    public const float StandardGravity = 9.80665f;

    private IntPtr _tracker;
    private IntPtr _zero;

    public MotionTracker()
    {
        _tracker = TrackerCreate();
        _zero = AccelZeroCreate();
        if (_tracker == IntPtr.Zero || _zero == IntPtr.Zero)
        {
            Dispose();
            throw new InvalidOperationException("chiaki_orientation_tracker_init failed.");
        }
    }

    /// <summary>SDL's milliseconds as the microseconds the tracker counts in.</summary>
    public static uint ToMicroseconds(uint sdlMilliseconds) => sdlMilliseconds * 1000;

    /// <summary>SDL's m/s² as the g the console reads. Acceleration only - see the note above.</summary>
    public static float ToGravities(float metresPerSecondSquared)
        => metresPerSecondSquared / StandardGravity;

    /// <summary>
    /// An accelerometer sample. The values arrive in m/s² and are normalised here, and the zero
    /// is updated first - the Qt client sets it active on every real accel sample, so a pad that
    /// is moved and then held still recalibrates rather than staying tilted.
    /// </summary>
    public void Accelerometer(float x, float y, float z, uint sdlTimestampMs)
    {
        float ax = ToGravities(x);
        float ay = ToGravities(y);
        float az = ToGravities(z);

        AccelZeroSetActive(_zero, ax, ay, az, true);
        ReadState(out float[] gyro, out _, out _, out _);
        TrackerUpdate(Handle, gyro[0], gyro[1], gyro[2], ax, ay, az, _zero,
            false, ToMicroseconds(sdlTimestampMs));
    }

    /// <summary>
    /// A gyroscope sample. NOT divided by gravity: it is an angular velocity, and the console
    /// wants it as it comes.
    /// </summary>
    public void Gyroscope(float x, float y, float z, uint sdlTimestampMs)
    {
        ReadState(out _, out float[] accel, out _, out _);
        TrackerUpdate(Handle, x, y, z, accel[0], accel[1], accel[2], _zero,
            true, ToMicroseconds(sdlTimestampMs));
    }

    /// <summary>The current orientation, which is what the console is told.</summary>
    public Orientation Current
    {
        get
        {
            ReadState(out _, out _, out float[] orient, out _);
            return new Orientation(orient[0], orient[1], orient[2], orient[3]);
        }
    }

    /// <summary>The timestamp of the last sample the tracker accepted, in microseconds.</summary>
    public uint Timestamp
    {
        get
        {
            ReadState(out _, out _, out _, out uint ts);
            return ts;
        }
    }

    /// <summary>Writes the orientation into a controller state, which is what gets sent.</summary>
    public void ApplyTo(ChiakiControllerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        TrackerApply(Handle, state.Handle);
    }

    /// <summary>Stops applying a calibration, without forgetting the tracker's own state.</summary>
    public void ClearZero() => AccelZeroSetInactive(_zero, true);

    private IntPtr Handle
        => _tracker != IntPtr.Zero ? _tracker : throw new ObjectDisposedException(nameof(MotionTracker));

    private void ReadState(out float[] gyro, out float[] accel, out float[] orient, out uint timestamp)
    {
        gyro = new float[3];
        accel = new float[3];
        orient = new float[4];
        if (!TrackerRead(Handle, gyro, accel, orient, out timestamp))
            throw new InvalidOperationException("chiaki_shim_orientation_tracker_read failed.");
    }

    public void Dispose()
    {
        if (_tracker != IntPtr.Zero)
        {
            TrackerFree(_tracker);
            _tracker = IntPtr.Zero;
        }

        if (_zero != IntPtr.Zero)
        {
            AccelZeroFree(_zero);
            _zero = IntPtr.Zero;
        }
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_orientation_tracker_create",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr TrackerCreate();

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_orientation_tracker_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void TrackerFree(IntPtr tracker);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_accel_new_zero_create",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr AccelZeroCreate();

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_accel_new_zero_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void AccelZeroFree(IntPtr accelZero);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_accel_new_zero_set_active",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void AccelZeroSetActive(
        IntPtr accelZero, float x, float y, float z, [MarshalAs(UnmanagedType.I1)] bool realAccel);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_accel_new_zero_set_inactive",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void AccelZeroSetInactive(
        IntPtr accelZero, [MarshalAs(UnmanagedType.I1)] bool realAccel);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_orientation_tracker_update",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void TrackerUpdate(
        IntPtr tracker, float gx, float gy, float gz, float ax, float ay, float az,
        IntPtr accelZero, [MarshalAs(UnmanagedType.I1)] bool zeroApplied, uint timestampUs);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_orientation_tracker_apply",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void TrackerApply(IntPtr tracker, IntPtr state);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_orientation_tracker_read",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool TrackerRead(
        IntPtr tracker, float[] gyro, float[] accel, float[] orient, out uint timestamp);
}

/// <summary>
/// PP130: the two conversions on the way in, read out of the Qt client so the port cannot drift.
/// </summary>
public static partial class MotionSource
{
    /// <summary>The Qt client's controller code.</summary>
    public const string RelativePath = @"gui\src\controllermanager.cpp";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Whether acceleration is still the sensor divided by gravity - all three axes.</summary>
    public static bool AccelIsDividedByGravity(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return GravityRegex().Matches(text).Count == 3;
    }

    /// <summary>
    /// Whether the gyroscope is still taken raw. The two sensors arrive through one event type,
    /// so normalising both is one line and no error - this is what says only one is.
    /// </summary>
    public static bool GyroIsTakenRaw(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return RawGyroRegex().Matches(text).Count == 3;
    }

    /// <summary>Whether the timestamp is still multiplied to microseconds.</summary>
    public static bool TimestampIsMicroseconds(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Contains("event.timestamp * 1000", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"accel_[xyz] = event\.data\[\d\] / SDL_STANDARD_GRAVITY;")]
    private static partial Regex GravityRegex();

    [GeneratedRegex(@"gyro_[xyz] = event\.data\[\d\];")]
    private static partial Regex RawGyroRegex();
}

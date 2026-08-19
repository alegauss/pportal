using System.Runtime.InteropServices;
using System.Text;
using ChiakiNg.Native;
using ChiakiNg.Settings;

namespace ChiakiNg.Session;

/// <summary>
/// PP5: StreamSession::FillBaseline, without the QElapsedTimer around it.
///
/// This is the ledger the two clients are compared with, so the one thing this side must not do is
/// write its own JSON. A second formatter drifts a key or a rounding, and then the rows stop being
/// comparable - which is the only thing the file is for. So the record is libchiaki's struct,
/// filled through the shim and formatted by chiaki_session_baseline_format: the .NET host
/// contributes rows, not a format.
///
/// The schema number is pinned here for the same reason. A libchiaki that bumps it turns
/// <see cref="Schema"/> red rather than quietly appending rows a reader mixes with the old ones.
///
/// What the record deliberately has no room for is a console name, an address, a session id or an
/// account. Those are exactly the fields the session log needs
/// <see cref="SessionLogSanitizer"/> to remove, so the baseline never collects them - and the
/// guard against a later uploader is that there is nothing here worth transmitting.
/// </summary>
public sealed class SessionBaseline : IDisposable
{
    /// <summary>Must equal CHIAKI_SESSION_BASELINE_SCHEMA. Asserted against the shim on every run.</summary>
    public const uint ExpectedSchema = 5;

    private IntPtr _handle;

    public SessionBaseline()
    {
        _handle = BaselineCreate();
        if (_handle == IntPtr.Zero)
            throw new OutOfMemoryException("chiaki_shim_baseline_create returned null.");
    }

    private IntPtr Handle
        => _handle != IntPtr.Zero ? _handle : throw new ObjectDisposedException(nameof(SessionBaseline));

    /// <summary>The schema libchiaki actually compiled, which is what <see cref="ExpectedSchema"/> is held against.</summary>
    public static uint Schema => BaselineSchema();

    /// <summary>The ledger both builds append to: chiaki_baseline.jsonl, inside the log directory.</summary>
    public static string LedgerPath => QtPaths.SessionBaselineFile;

    /// <summary>Taken rather than read off the clock, so a record can be reproduced.</summary>
    public void SetStarted(DateTimeOffset startedUtc)
        => BaselineSetStarted(Handle, (ulong)startedUtc.ToUnixTimeSeconds());

    public void SetDuration(TimeSpan duration)
        => BaselineSetDuration(Handle, (ulong)Math.Max(0, (long)duration.TotalMilliseconds));

    public void SetAppVersion(string version) => BaselineSetAppVersion(Handle, version);

    /// <summary>What was asked for. The measured bitrate is a shortfall against this one.</summary>
    public void SetVideo(string codec, uint width, uint height, uint fps, uint bitrateKbps)
        => BaselineSetVideo(Handle, codec, width, height, fps, bitrateKbps);

    /// <summary>
    /// The settings that explain the numbers beside them. The renderer travels with the decoder
    /// because it is what narrowed the decoder choice: two rows naming different decoders are only
    /// comparable once both name the renderer that allowed them.
    /// </summary>
    public void SetConfig(string hwDecoder, string renderer, double packetLossMax, bool idrOnFecFailure)
        => BaselineSetConfig(Handle, hwDecoder, renderer, packetLossMax, idrOnFecFailure);

    public void SetMeasured(
        double measuredBitrateMbps, double averagePacketLoss,
        ulong framesPresented, ulong framesLost, ulong framesDropped, ulong networkRttUs)
        => BaselineSetMeasured(Handle, measuredBitrateMbps, averagePacketLoss,
            framesPresented, framesLost, framesDropped, networkRttUs);

    public void PushHandoff(ulong handoffUs) => BaselinePushHandoff(Handle, handoffUs);

    public void PushInputToWire(ulong inputUs) => BaselinePushInputToWire(Handle, inputUs);

    public ulong HandoffAverageUs => BaselineHandoffAvgUs(Handle);

    /// <summary>
    /// Input queueing plus the network round trip plus the handoff. A floor on glass to glass and
    /// not a measurement of it: the console's input handling, the game's render, the encoder and
    /// the panel are all outside this process and none of them is in this number.
    /// </summary>
    public ulong LatencyEstimateUs => BaselineLatencyEstimateUs(Handle);

    /// <summary>The line as the Qt build writes it, newline included.</summary>
    public string Format()
    {
        int max = BaselineLineMax();
        byte[] buf = new byte[max];
        int err = BaselineFormat(Handle, buf, max, out int written);
        if (err != (int)ChiakiError.Success)
            throw new InvalidOperationException(
                $"chiaki_session_baseline_format failed: {ChiakiNative.ErrorString(err) ?? err.ToString()}");

        return Encoding.UTF8.GetString(buf, 0, written);
    }

    /// <summary>Appends one row to the ledger, creating the file if it is not there.</summary>
    public ChiakiError AppendTo(string path) => (ChiakiError)BaselineAppend(Handle, path);

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;

        BaselineFree(_handle);
        _handle = IntPtr.Zero;
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_schema",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern uint BaselineSchema();

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_line_max",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int BaselineLineMax();

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_create",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr BaselineCreate();

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void BaselineFree(IntPtr baseline);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_set_started",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void BaselineSetStarted(IntPtr baseline, ulong unixSeconds);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_set_duration_ms",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void BaselineSetDuration(IntPtr baseline, ulong durationMs);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_set_app_version",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void BaselineSetAppVersion(
        IntPtr baseline, [MarshalAs(UnmanagedType.LPUTF8Str)] string version);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_set_video",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void BaselineSetVideo(
        IntPtr baseline, [MarshalAs(UnmanagedType.LPUTF8Str)] string codec,
        uint width, uint height, uint fps, uint bitrateKbps);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_set_config",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void BaselineSetConfig(
        IntPtr baseline,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string hwDecoder,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string renderer,
        double packetLossMax,
        [MarshalAs(UnmanagedType.I1)] bool idrOnFecFailure);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_set_measured",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void BaselineSetMeasured(
        IntPtr baseline, double measuredBitrateMbps, double averagePacketLoss,
        ulong framesPresented, ulong framesLost, ulong framesDropped, ulong networkRttUs);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_push_handoff",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void BaselinePushHandoff(IntPtr baseline, ulong handoffUs);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_push_input_to_wire",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void BaselinePushInputToWire(IntPtr baseline, ulong inputUs);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_handoff_avg_us",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong BaselineHandoffAvgUs(IntPtr baseline);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_latency_estimate_us",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong BaselineLatencyEstimateUs(IntPtr baseline);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_format",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int BaselineFormat(IntPtr baseline, byte[] buf, int bufSize, out int written);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_append",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int BaselineAppend(
        IntPtr baseline, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
}

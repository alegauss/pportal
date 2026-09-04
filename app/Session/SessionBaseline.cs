using System.Runtime.InteropServices;
using System.Text;
using ChiakiNg.Native;
using ChiakiNg.Settings;

namespace ChiakiNg.Session;

/// <summary>
/// PP23: the four stages before the handoff, plus the decode.
///
/// Five and not six. The present stage is the handoff, which the baseline keeps separately - it is
/// in the latency estimate, and a stage counted there as well would inflate a number a session is
/// judged by. Named <c>FrameStageTimer</c> rather than FrameStage because
/// <see cref="ChiakiNg.Protocol.FrameStage"/> already means the frame processor's two, and the two
/// sets are not the same thing.
/// </summary>
public enum FrameStageTimer
{
    Receive = 0,
    Reorder = 1,
    Reassemble = 2,
    Correct = 3,
    Decode = 4,
}

/// <summary>
/// PP23: one baseline statistic, and the number the average hides.
///
/// <see cref="SessionBaseline"/> exposes an average and nothing else, and sessionbaseline.h itself
/// says what that costs: ten stalls in a thousand frames drag the mean to 1990us while ninety-nine
/// percent of frames were at 1000. The mean overstates the typical frame by two and understates the
/// worst by fifty, in one number - which is why the statistic keeps a histogram rather than a
/// running total, and why a port that could only read the mean could only report the misleading
/// half.
///
/// The percentile is a BOUND and says so. The buckets are eight to the octave, so the answer is
/// the upper edge of the bucket the true value falls in: never below it, and within 12.5% above.
/// Past the last bucket it is clamped to the observed maximum, which is why a five-second stall
/// reads as five seconds rather than as the top of the histogram.
/// </summary>
public sealed class BaselineStat : IDisposable
{
    private IntPtr handle;

    /// <summary>An empty statistic, which answers zero to everything until something is pushed.</summary>
    public BaselineStat()
    {
        handle = StatCreate();
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("chiaki_shim_baseline_stat_create returned null.");
    }

    private IntPtr Handle
        => handle != IntPtr.Zero ? handle : throw new ObjectDisposedException(nameof(BaselineStat));

    /// <summary>One sample, folded into the histogram as it arrives.</summary>
    public void Push(ulong sampleUs) => StatPush(Handle, sampleUs);

    /// <summary>How many samples have been folded in.</summary>
    public ulong Samples => StatSamples(Handle);

    /// <summary>The fastest sample seen, or zero when none has been.</summary>
    public ulong MinimumUs => StatMin(Handle);

    /// <summary>And the slowest, which is what the percentile is clamped to.</summary>
    public ulong MaximumUs => StatMax(Handle);

    /// <summary>The mean. Kept, and not to be read on its own - see the class note.</summary>
    public ulong AverageUs => StatAvg(Handle);

    /// <summary>The median, as a bucket bound.</summary>
    public ulong P50Us => StatP50(Handle);

    /// <summary>The ninety-ninth percentile, which is the number a stream is judged by.</summary>
    public ulong P99Us => StatP99(Handle);

    /// <summary>Any percentile, so the two named ones cannot drift from the general one.</summary>
    public ulong PercentileUs(uint percent) => StatPercentile(Handle, percent);

    public void Dispose()
    {
        if (handle == IntPtr.Zero)
            return;

        StatFree(handle);
        handle = IntPtr.Zero;
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_stat_create",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr StatCreate();

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_stat_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void StatFree(IntPtr stat);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_stat_push",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void StatPush(IntPtr stat, ulong sampleUs);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_stat_samples",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong StatSamples(IntPtr stat);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_stat_min_us",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong StatMin(IntPtr stat);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_stat_max_us",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong StatMax(IntPtr stat);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_stat_avg",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong StatAvg(IntPtr stat);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_stat_p50_us",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong StatP50(IntPtr stat);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_stat_p99_us",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong StatP99(IntPtr stat);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_stat_percentile_us",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong StatPercentile(IntPtr stat, uint percent);
}

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

    /// <summary>
    /// PP23: one sample into one frame stage.
    ///
    /// The five were carried in the line and unfillable from here until now, so a session wrote
    /// five zeros whatever it did. That is worse than writing them wrongly: the C's fixture pushes
    /// a distinguishable value per stage because a stage filed under another stage's name is the
    /// defect it exists to catch, and zeros cannot be caught by it.
    /// </summary>
    public void PushStage(FrameStageTimer stage, ulong sampleUs)
        => BaselinePushStage(Handle, (int)stage, sampleUs);

    /// <summary>How many samples one stage holds, which is what makes "separate" assertable.</summary>
    public ulong StageSamples(FrameStageTimer stage) => BaselineStageSamples(Handle, (int)stage);

    /// <summary>
    /// And the handoff's, which is the PRESENT stage and not a sixth accumulator - a caller that
    /// pushed it as a stage would count it twice, once there and once in the latency estimate.
    /// </summary>
    public ulong HandoffSamples => BaselineHandoffSamples(Handle);

    public void PushInputToWire(ulong inputUs) => BaselinePushInputToWire(Handle, inputUs);

    public ulong HandoffAverageUs => BaselineHandoffAvgUs(Handle);

    /// <summary>
    /// Input queueing plus the network round trip plus the handoff. A floor on glass to glass and
    /// not a measurement of it: the console's input handling, the game's render, the encoder and
    /// the panel are all outside this process and none of them is in this number.
    /// </summary>
    public ulong LatencyEstimateUs => BaselineLatencyEstimateUs(Handle);

    /// <summary>
    /// PP76: frames_dropped less frames_lost, which is the only decoder-attributable loss here.
    ///
    /// NEITHER COUNTER IS A DECODER'S OWN, and that is the whole reason this is a subtraction rather
    /// than a reading. frames_lost is the video receiver's total, counted upstream of every decoder
    /// - a frame the network lost is charged to it whichever decoder was going to receive one. And
    /// frames_dropped is what the presenter never showed, which includes those.
    ///
    /// So the difference is a FLOOR on what the decoder itself lost, and PP76's own line says that
    /// reading either alone is what it exists to prevent.
    ///
    /// Clamped rather than wrapped, which is the C's decision and not a defensive one: the two are
    /// sampled by different threads at different moments, so the receiver can legitimately be ahead
    /// of the presenter at a session's end. Subtracting unsigned would turn a few frames of skew
    /// into eighteen quintillion.
    /// </summary>
    public ulong DecoderDrops => BaselineDecoderDrops(Handle);

    [System.Runtime.InteropServices.DllImport(Native.ChiakiNative.Library,
        EntryPoint = "chiaki_shim_baseline_decoder_drops",
        CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern ulong BaselineDecoderDrops(IntPtr baseline);

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

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_push_stage",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void BaselinePushStage(IntPtr baseline, int stage, ulong sampleUs);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_stage_samples",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong BaselineStageSamples(IntPtr baseline, int stage);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_baseline_handoff_samples",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong BaselineHandoffSamples(IntPtr baseline);

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

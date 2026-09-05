using ChiakiNg.Native;

namespace ChiakiNg.Session;

/// <summary>What one pass through the cleaning stage produced.</summary>
/// <param name="Cleaned">Bytes at the announced rate, or zero while the filter is still filling.</param>
/// <param name="AtCancellerRate">What the canceller itself handed back, before the way up.</param>
public readonly record struct CleanedUnit(int Cleaned, int AtCancellerRate);

/// <summary>
/// PP52's second criterion: a stage between the capture and the encoder that actually cleans.
///
/// PP709 drove Windows's Voice Capture DSP and found it refuses the 48000 the console is told
/// about, taking 22050 and below. PP710 brought the bridge - the in-box resampler, the same DMO
/// shape - and PP706 built the path a stage can sit in. This is the stage.
///
/// THE ARITHMETIC PICKS THE RATE, and it is asked rather than written down. The transform says
/// which rates it takes and the unit length says which of those a whole frame survives - the path
/// moves in ten-millisecond units, and 22050 gives 220.5 samples while 11025 gives 110.25. On the
/// machine PP709 measured that leaves 16000 and 8000, so the canceller runs at the SECOND of its
/// four rates rather than the best, and the reason is the unit and not the transform.
///
/// THREE RESAMPLERS AND NOT ONE. Both inputs come down and the cleaned stream goes back up, and
/// each direction is a converter with state of its own - a filter shared between the microphone and
/// the reference would carry one signal's tail into the other's. The capture engine could do the
/// two downward legs for nothing, as PP710 recorded, but only for a caller that asks its ENDPOINTS
/// for 16000 - and WasapiCapture asks for the announced format. This takes bytes rather than
/// devices, which is what makes it testable without one.
///
/// PP648'S RULE IS WHY <see cref="CleanedUnit"/> HAS TWO NUMBERS. Every call in here can succeed
/// while nothing is cancelled, so what is reported is what came BACK from the canceller and what
/// came back from the way up. A pass that produced nothing is a state this returns, and the caller
/// sends nothing rather than sending the microphone raw and calling it cleaned.
///
/// THERE IS NO ABSENCE TO BE QUIET ABOUT, which is the criterion's own distinction. The transform
/// is in Windows; a machine without it answers false from <see cref="Start"/> and the caller keeps
/// the microphone it already had. That is not the hardware contract's quiet vendor fallback - it is
/// the ordinary shape every device path in this port has.
/// </summary>
public sealed class CleanedMicrophone : IDisposable
{
    /// <summary>
    /// The rate this path expects to end up at, which is documentation rather than the answer.
    ///
    /// <see cref="Start"/> asks the transform which rates it takes and keeps the highest that a
    /// whole unit survives, so the number below is what that came to on the machine PP709 measured.
    /// A machine whose transform answers differently gets a different rate and no edit.
    /// </summary>
    public const int ExpectedCancellerRate = 16000;

    private readonly MicrophoneAnnouncement announced;
    private readonly VoiceCaptureDsp canceller = new();
    private readonly AudioResampler microphoneDown = new();
    private readonly AudioResampler referenceDown = new();
    private readonly AudioResampler cleanedUp = new();

    private MicrophoneUnits? microphoneFrames;
    private MicrophoneUnits? referenceFrames;

    private byte[] downMicrophone = [];
    private byte[] downReference = [];
    private byte[] cancelled = [];
    private byte[] pendingMicrophone = [];
    private byte[] pendingReference = [];

    private bool started;
    private bool disposed;

    /// <param name="announced">The format the path moves in, which is the console's.</param>
    public CleanedMicrophone(MicrophoneAnnouncement? announced = null)
    {
        this.announced = announced ?? MicrophoneFormat.Announced;
    }

    /// <summary>A unit at the announced rate, which is what goes in and comes out.</summary>
    public int UnitBytes => MicrophoneFormat.BytesPerUnit(announced);

    /// <summary>The rate the transform was actually configured at, or zero before Start.</summary>
    public int CancellerRate { get; private set; }

    /// <summary>And a unit at it, which is what the transform is fed.</summary>
    public int CancellerFrameBytes => FrameBytesAt(CancellerRate);

    /// <summary>A whole unit at one rate, in bytes.</summary>
    private int FrameBytesAt(int rate)
        => (int)(rate * MicrophoneFormat.UnitMilliseconds(announced) / 1000)
            * announced.Channels * MicrophoneFormat.BytesPerSample(announced);

    /// <summary>Whether the stage is running. False on a machine without the transform.</summary>
    public bool Running => started;

    /// <summary>How many units the canceller actually returned something for.</summary>
    public int UnitsCleaned { get; private set; }

    /// <summary>How many it returned nothing for, which is the filter filling and then loss.</summary>
    public int UnitsWithNothingBack { get; private set; }

    /// <summary>What last refused, or zero.</summary>
    public int LastError { get; private set; }

    /// <summary>
    /// Whether this machine has the transform at all, without building a stage.
    /// </summary>
    public static bool IsAvailable()
    {
        using var probe = new VoiceCaptureDsp();
        return probe.Create();
    }

    /// <summary>
    /// The canceller's rates that divide into a whole number of samples per unit.
    ///
    /// Derived rather than listed: a rate that does not divide into the unit's length cannot fill
    /// one. Which rates go in is the caller's - <see cref="Start"/> passes what the transform said it
    /// accepts - so this is arithmetic over a machine's answer rather than a claim about either.
    /// </summary>
    public static IReadOnlyList<int> RatesWholeUnitsSurvive(
        IEnumerable<int> rates, MicrophoneAnnouncement announced)
    {
        ArgumentNullException.ThrowIfNull(rates);

        double milliseconds = MicrophoneFormat.UnitMilliseconds(announced);

        return
        [
            .. rates
                .Where(rate => Math.Abs((rate * milliseconds / 1000) % 1) < 1e-9)
                .Order()
                .Reverse(),
        ];
    }

    /// <summary>
    /// Build the transform and the three converters, or report that this machine has none.
    /// </summary>
    public bool Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (started)
            return true;

        if (!canceller.Create())
        {
            LastError = canceller.LastError;
            return false;
        }

        // ASKED, not chosen here. The transform says which rates it takes and the unit length says
        // which of those a whole frame survives; the highest of what is left is the best this path
        // can do, and on a machine that answers differently it is a different number and no edit.
        IReadOnlyList<int> usable = RatesWholeUnitsSurvive(
            canceller.Accepts().Where(one => one.Accepted).Select(one => one.Rate), announced);

        if (usable.Count == 0 || !canceller.Configure(usable[0]))
        {
            LastError = canceller.LastError;
            return false;
        }

        CancellerRate = usable[0];

        foreach ((AudioResampler resampler, int from, int to) in Legs())
        {
            if (!resampler.Create() || !resampler.Configure(from, to))
            {
                LastError = resampler.LastError;
                return false;
            }
        }

        // Sized once, here, so nothing on the per-unit path allocates. The downward legs produce a
        // little more than a frame while their filters settle, so the scratch is generous.
        downMicrophone = new byte[CancellerFrameBytes * 4];
        downReference = new byte[CancellerFrameBytes * 4];
        cancelled = new byte[CancellerFrameBytes * 4];
        pendingMicrophone = new byte[CancellerFrameBytes];
        pendingReference = new byte[CancellerFrameBytes];

        microphoneFrames = new MicrophoneUnits(CancellerFrameBytes);
        referenceFrames = new MicrophoneUnits(CancellerFrameBytes);

        started = true;
        return true;
    }

    /// <summary>
    /// One unit of microphone and the same span of what was playing, cleaned.
    /// </summary>
    /// <param name="microphone">A unit at the announced rate.</param>
    /// <param name="reference">The same, of the render side. Empty is silence.</param>
    /// <param name="into">Where the cleaned unit goes; its length is the maximum taken.</param>
    /// <returns>What the canceller and the way back up produced.</returns>
    public CleanedUnit Clean(
        ReadOnlySpan<byte> microphone, ReadOnlySpan<byte> reference, Span<byte> into)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (!started || microphoneFrames is null || referenceFrames is null)
            return default;

        int down = microphoneDown.Process(microphone, downMicrophone).Produced;
        int downRef = reference.IsEmpty
            ? Silence(downReference)
            : referenceDown.Process(reference, downReference).Produced;

        return Cancel(down, downRef, into);
    }

    /// <summary>
    /// PP711: the same, for a caller whose endpoints are already at <see cref="CancellerRate"/>.
    ///
    /// AUTOCONVERTPCM means a capture can ask its endpoint for any format, so a host that opens the
    /// microphone and PP698's reference at the canceller's rate has already paid for the two
    /// downward legs inside the engine - and this door skips them rather than converting bytes that
    /// are already converted.
    ///
    /// The way UP is still here, because nothing is playing the cleaned stream: it goes to an
    /// encoder that was told 48000, and no engine sits between the two.
    ///
    /// <see cref="Start"/> has to have run before a caller knows what rate to open at, which is the
    /// order this door imposes and the reason the bytes-in one stays: an assertion that had to open
    /// a device first could not run on a machine without one.
    /// </summary>
    /// <param name="microphone">A whole <see cref="CancellerFrameBytes"/> frame, or more.</param>
    /// <param name="reference">The same, of the render side. Empty is silence.</param>
    /// <param name="into">Where the cleaned unit goes, at the announced rate.</param>
    public CleanedUnit CleanAtCancellerRate(
        ReadOnlySpan<byte> microphone, ReadOnlySpan<byte> reference, Span<byte> into)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (!started || microphoneFrames is null || referenceFrames is null)
            return default;

        int down = Math.Min(microphone.Length, downMicrophone.Length);
        microphone[..down].CopyTo(downMicrophone);

        int downRef;
        if (reference.IsEmpty)
        {
            downRef = Silence(downReference);
        }
        else
        {
            downRef = Math.Min(reference.Length, downReference.Length);
            reference[..downRef].CopyTo(downReference);
        }

        return Cancel(down, downRef, into);
    }

    /// <summary>
    /// The half both doors share: whole frames to the transform, then the way back up.
    /// </summary>
    private CleanedUnit Cancel(int down, int downRef, Span<byte> into)
    {
        // The converters hand back whatever is ready, which is not a whole frame every time. The
        // canceller wants whole frames, so the two streams are accumulated to its own unit before
        // either reaches it. The device-rate door goes through the same accumulator: an endpoint
        // delivers whole units, but a caller batching two of them would otherwise skip a frame.
        byte[] pendingMic = pendingMicrophone;
        byte[] pendingRef = pendingReference;

        var micFrames = new List<byte[]>();
        var refFrames = new List<byte[]>();

        microphoneFrames!.Take(downMicrophone.AsSpan(0, down), one => micFrames.Add(one.ToArray()));
        referenceFrames!.Take(downReference.AsSpan(0, downRef), one => refFrames.Add(one.ToArray()));

        var atCancellerRate = 0;

        for (var i = 0; i < micFrames.Count; i++)
        {
            byte[] mic = micFrames[i];
            byte[] speaker = i < refFrames.Count ? refFrames[i] : pendingRef;

            DspPass pass = canceller.Process(mic, speaker, cancelled.AsSpan(atCancellerRate));
            atCancellerRate += pass.Cleaned;
        }

        // Kept so an uneven pass has something to line up against next time, which is the whole of
        // the alignment this stage can do without a clock on either side.
        if (micFrames.Count > 0)
            micFrames[^1].CopyTo(pendingMic, 0);

        if (refFrames.Count > 0)
            refFrames[^1].CopyTo(pendingRef, 0);

        if (atCancellerRate == 0)
        {
            UnitsWithNothingBack++;
            return new CleanedUnit(0, 0);
        }

        int up = cleanedUp.Process(cancelled.AsSpan(0, atCancellerRate), into).Produced;

        if (up > 0)
            UnitsCleaned++;
        else
            UnitsWithNothingBack++;

        return new CleanedUnit(up, atCancellerRate);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        started = false;
        CancellerRate = 0;

        canceller.Dispose();
        microphoneDown.Dispose();
        referenceDown.Dispose();
        cleanedUp.Dispose();
    }

    /// <summary>The three converters and which way each goes.</summary>
    private IEnumerable<(AudioResampler Resampler, int From, int To)> Legs()
    {
        yield return (microphoneDown, announced.Rate, CancellerRate);
        yield return (referenceDown, announced.Rate, CancellerRate);
        yield return (cleanedUp, CancellerRate, announced.Rate);    }

    /// <summary>
    /// A frame of silence for the reference, which is what a quiet render endpoint delivers.
    ///
    /// PP698 measured that a loopback client on an endpoint playing nothing produces NO packets at
    /// all rather than zeroes - so a caller with nothing to hand over hands over nothing, and the
    /// canceller still needs a frame on its second input. Zeroes are the honest one: there was no
    /// echo, so there is nothing to subtract.
    /// </summary>
    private int Silence(byte[] into)
    {
        Array.Clear(into, 0, CancellerFrameBytes);
        return CancellerFrameBytes;
    }
}

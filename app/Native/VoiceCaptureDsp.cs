using System.Runtime.InteropServices;
using ChiakiNg.Session;

namespace ChiakiNg.Native;

/// <summary>What the Voice Capture DSP said when it was asked for a format.</summary>
/// <param name="Rate">Samples a second.</param>
/// <param name="Accepted">Whether the transform takes it as its output.</param>
/// <param name="HResult">What it answered, so a no is refutable.</param>
public readonly record struct DspFormatAnswer(int Rate, bool Accepted, int HResult);

/// <summary>What one pass through the transform did.</summary>
/// <param name="Fed">Bytes handed to the microphone stream.</param>
/// <param name="Reference">Bytes handed to the reference stream.</param>
/// <param name="Cleaned">Bytes the transform gave back. Zero is a legitimate first answer.</param>
public readonly record struct DspPass(int Fed, int Reference, int Cleaned);

/// <summary>
/// PP52, under PP698: Windows's own echo canceller, driven in filter mode.
///
/// spike/audio-effects answered the prior question - NVIDIA's audio effects SDK is not reachable on
/// a machine with the card, and CLSID_CWMAudioAEC is registered in both hives with mfwmaaec.dll
/// present. This is the second half of the line: a stage that actually cleans a sample.
///
/// FILTER MODE, WHICH IS WHY PP698 CAME FIRST. The DSP has two shapes. In SOURCE mode it opens both
/// devices itself and hands back one cleaned stream - which replaces WasapiCapture and takes the
/// device choice with it, and PP695 is why this port keeps that choice. In FILTER mode the host
/// feeds both streams: the microphone on input 0 and a reference of what is playing on input 1.
/// PP698 built the second, and this is what consumes it.
///
/// IT IS READ BACK, NOT ASSUMED TO HAVE RUN. PP648's rule, and this transform is exactly the shape
/// it was written for: every call here can succeed while nothing is cancelled. So
/// <see cref="Accepts"/> asks the DSP which formats it will actually take rather than trusting a
/// documented list, and <see cref="Process"/> reports how many bytes came BACK - a pass that
/// produced nothing is a fact this returns rather than an exception it hides.
///
/// EVERY METHOD CARRIES PreserveSig, which PP693 made a check rather than a habit: without it the
/// CLR reads the declared int as a retval and every HRESULT test compares against an uninitialised
/// local. A DMO returns S_FALSE for "nothing yet" on more than one method, so that matters here more
/// than most places - a non-zero success would be read as a failure and a failure as success.
/// </summary>
public sealed class VoiceCaptureDsp : IDisposable
{
    /// <summary>The mic and the reference, which is what filter mode means.</summary>
    public const int InputStreams = 2;

    /// <summary>The cleaned microphone.</summary>
    public const int OutputStreams = 1;

    /// <summary>Which input carries the microphone.</summary>
    public const int MicrophoneStream = 0;

    /// <summary>And which carries what the speakers are playing.</summary>
    public const int ReferenceStream = 1;

    /// <summary>
    /// The rates worth asking about: the announced one, and the four the transform is documented
    /// to produce.
    ///
    /// Asked rather than assumed, which is the whole point. A list transcribed from documentation is
    /// a claim about a version of Windows, and this port has one in front of it.
    /// </summary>
    public static IReadOnlyList<int> CandidateRates { get; } = [48000, 22050, 16000, 11025, 8000];

    private object? instance;
    private Dmo.IMediaObject? media;
    private bool configured;

    /// <summary>The rate the transform was configured at, or zero.</summary>
    public int Rate { get; private set; }

    /// <summary>What last refused, or zero.</summary>
    public int LastError { get; private set; }

    /// <summary>Whether the object exists at all, which is one CoCreateInstance.</summary>
    public bool Created => media is not null;

    /// <summary>
    /// Create the transform and put it in filter mode with acoustic echo cancellation on.
    /// </summary>
    /// <returns>Whether it was created and configured. A machine without it answers false.</returns>
    public bool Create()
    {
        if (media is not null)
            return true;

        instance = Dmo.Create(new Guid(EchoCancellation.VoiceCaptureDspClsid), out int hresult);
        LastError = hresult;

        if (instance is null)
            return false;

        if (instance is not Dmo.IPropertyStore store || instance is not Dmo.IMediaObject asMedia)
        {
            Release();
            return false;
        }

        // SOURCE MODE FIRST AND FALSE, because it decides which of the two shapes the object is and
        // every other property is read against that shape.
        if (!Dmo.SetBool(store, SourceModeProperty, value: false))
        {
            Release();
            return false;
        }

        // SINGLE_CHANNEL_AEC: one microphone, one reference, echo cancellation. The array modes are
        // for a microphone array this port has no way to know it has.
        if (!Dmo.SetInt(store, SystemModeProperty, SingleChannelAec))
        {
            Release();
            return false;
        }

        media = asMedia;
        return true;
    }

    /// <summary>
    /// Which of the candidate rates the transform will take as its output, asked one at a time.
    ///
    /// Dmo.SetTypeTestOnly, so nothing is configured by asking. The answer is the machine's
    /// rather than the documentation's, which is the difference between a reading and a citation.
    /// </summary>
    public IReadOnlyList<DspFormatAnswer> Accepts()
    {
        if (media is not { } transform)
            return [];

        var answers = new List<DspFormatAnswer>();

        foreach (int rate in CandidateRates)
        {
            IntPtr type = Dmo.MediaType(rate);
            try
            {
                int hr = transform.SetOutputType(0, type, Dmo.SetTypeTestOnly);
                answers.Add(new DspFormatAnswer(rate, hr == 0, hr));
            }
            finally
            {
                Dmo.FreeMediaType(type);
            }
        }

        return answers;
    }

    /// <summary>
    /// Configure both inputs and the output at one rate, which is what makes it runnable.
    ///
    /// The transform requires all three to agree; there is no arm here that lets them differ,
    /// because a reference at a different rate is a subtraction against the wrong samples and the
    /// DSP would take it.
    /// </summary>
    public bool Configure(int rate)
    {
        if (media is not { } transform)
            return false;

        foreach (int stream in new[] { MicrophoneStream, ReferenceStream })
        {
            IntPtr type = Dmo.MediaType(rate);
            try
            {
                LastError = transform.SetInputType(stream, type, 0);
                if (LastError != 0)
                    return false;
            }
            finally
            {
                Dmo.FreeMediaType(type);
            }
        }

        IntPtr output = Dmo.MediaType(rate);
        try
        {
            LastError = transform.SetOutputType(0, output, 0);
            if (LastError != 0)
                return false;
        }
        finally
        {
            Dmo.FreeMediaType(output);
        }

        LastError = transform.AllocateStreamingResources();
        if (LastError != 0)
            return false;

        Rate = rate;
        configured = true;
        return true;
    }

    /// <summary>
    /// One pass: a frame of microphone, the same length of reference, and whatever comes back.
    /// </summary>
    /// <param name="microphone">16-bit mono PCM at <see cref="Rate"/>.</param>
    /// <param name="reference">The same, of what is being played.</param>
    /// <param name="into">Where the cleaned samples go. Its length is the maximum taken.</param>
    /// <returns>What each stream was handed and what came back.</returns>
    /// <remarks>
    /// A first pass commonly returns nothing. The transform has a filter to converge and a buffer to
    /// fill, so zero bytes out is a state and not an error - which is why this reports the count
    /// rather than asserting on it.
    /// </remarks>
    public DspPass Process(ReadOnlySpan<byte> microphone, ReadOnlySpan<byte> reference, Span<byte> into)
    {
        if (media is not { } transform || !configured)
            return default;

        using var mic = new Dmo.Buffer(microphone.Length);
        using var speaker = new Dmo.Buffer(reference.Length);
        using var cleaned = new Dmo.Buffer(into.Length);

        mic.Fill(microphone);
        speaker.Fill(reference);

        LastError = transform.ProcessInput(
            MicrophoneStream, mic, Dmo.InputSyncPoint, 0, 0);
        if (LastError < 0)
            return new DspPass(microphone.Length, 0, 0);

        LastError = transform.ProcessInput(
            ReferenceStream, speaker, Dmo.InputSyncPoint, 0, 0);
        if (LastError < 0)
            return new DspPass(microphone.Length, reference.Length, 0);

        var buffers = new Dmo.OutputDataBuffer[1];
        buffers[0].Buffer = cleaned;

        LastError = transform.ProcessOutput(0, 1, buffers, out _);
        if (LastError < 0)
            return new DspPass(microphone.Length, reference.Length, 0);

        int produced = cleaned.Read(into);
        return new DspPass(microphone.Length, reference.Length, produced);
    }

    /// <summary>The stream counts the object reports, which is what says filter mode took.</summary>
    public (int Inputs, int Outputs) StreamCounts()
    {
        if (media is not { } transform || transform.GetStreamCount(out int inputs, out int outputs) != 0)
            return (0, 0);

        return (inputs, outputs);
    }

    public void Dispose() => Release();

    private void Release()
    {
        if (media is not null && configured)
            media.FreeStreamingResources();

        media = null;
        configured = false;

        if (instance is not null)
        {
            Marshal.ReleaseComObject(instance);
            instance = null;
        }
    }

    /// <summary>MFPKEY_WMAAECMA_SYSTEM_MODE.</summary>
    private static readonly Dmo.PropertyKey SystemModeProperty =
        new(new Guid("6f52c567-0360-4bd2-9617-ccbf1421c939"), 2);

    /// <summary>MFPKEY_WMAAECMA_DMO_SOURCE_MODE, which must be set before anything else.</summary>
    private static readonly Dmo.PropertyKey SourceModeProperty =
        new(new Guid("6f52c567-0360-4bd2-9617-ccbf1421c939"), 3);

    /// <summary>SINGLE_CHANNEL_AEC: one microphone, one reference, echo cancellation.</summary>
    private const int SingleChannelAec = 0;
}

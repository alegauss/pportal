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
    private IMediaObject? media;
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

        Type? type = Type.GetTypeFromCLSID(new Guid(EchoCancellation.VoiceCaptureDspClsid));
        if (type is null)
            return false;

        try
        {
            instance = Activator.CreateInstance(type);
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or NotSupportedException)
        {
            LastError = error.HResult;
            return false;
        }

        if (instance is not IPropertyStore store || instance is not IMediaObject asMedia)
        {
            Release();
            return false;
        }

        // SOURCE MODE FIRST AND FALSE, because it decides which of the two shapes the object is and
        // every other property is read against that shape.
        if (!SetBool(store, SourceModeProperty, value: false))
        {
            Release();
            return false;
        }

        // SINGLE_CHANNEL_AEC: one microphone, one reference, echo cancellation. The array modes are
        // for a microphone array this port has no way to know it has.
        if (!SetInt(store, SystemModeProperty, SingleChannelAec))
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
    /// DMO_SET_TYPEF_TEST_ONLY, so nothing is configured by asking. The answer is the machine's
    /// rather than the documentation's, which is the difference between a reading and a citation.
    /// </summary>
    public IReadOnlyList<DspFormatAnswer> Accepts()
    {
        if (media is not { } transform)
            return [];

        var answers = new List<DspFormatAnswer>();

        foreach (int rate in CandidateRates)
        {
            IntPtr type = MediaType(rate);
            try
            {
                int hr = transform.SetOutputType(0, type, DMO_SET_TYPEF_TEST_ONLY);
                answers.Add(new DspFormatAnswer(rate, hr == 0, hr));
            }
            finally
            {
                FreeMediaType(type);
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
            IntPtr type = MediaType(rate);
            try
            {
                LastError = transform.SetInputType(stream, type, 0);
                if (LastError != 0)
                    return false;
            }
            finally
            {
                FreeMediaType(type);
            }
        }

        IntPtr output = MediaType(rate);
        try
        {
            LastError = transform.SetOutputType(0, output, 0);
            if (LastError != 0)
                return false;
        }
        finally
        {
            FreeMediaType(output);
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

        using var mic = new MediaBuffer(microphone.Length);
        using var speaker = new MediaBuffer(reference.Length);
        using var cleaned = new MediaBuffer(into.Length);

        mic.Fill(microphone);
        speaker.Fill(reference);

        LastError = transform.ProcessInput(
            MicrophoneStream, mic, DMO_INPUT_DATA_BUFFERF_SYNCPOINT, 0, 0);
        if (LastError < 0)
            return new DspPass(microphone.Length, 0, 0);

        LastError = transform.ProcessInput(
            ReferenceStream, speaker, DMO_INPUT_DATA_BUFFERF_SYNCPOINT, 0, 0);
        if (LastError < 0)
            return new DspPass(microphone.Length, reference.Length, 0);

        var buffers = new DmoOutputDataBuffer[1];
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

    private static bool SetBool(IPropertyStore store, PropertyKey key, bool value)
    {
        // VARIANT_TRUE is -1 and VARIANT_FALSE is 0, which is the one place a bool is not a bit.
        var variant = new PropVariant { Type = VT_BOOL, Value = value ? new IntPtr(-1) : IntPtr.Zero };
        PropertyKey local = key;

        return store.SetValue(ref local, ref variant) == 0;
    }

    private static bool SetInt(IPropertyStore store, PropertyKey key, int value)
    {
        var variant = new PropVariant { Type = VT_I4, Value = new IntPtr(value) };
        PropertyKey local = key;

        return store.SetValue(ref local, ref variant) == 0;
    }

    /// <summary>
    /// A DMO_MEDIA_TYPE for 16-bit mono PCM at one rate, allocated whole.
    ///
    /// One block holding the type and the WAVEFORMATEX it points at, so freeing it is one call and
    /// there is no partial state to get wrong. MoInitMediaType would allocate the format for us and
    /// hand back memory MoFreeMediaType owns; owning both here is fewer rules.
    /// </summary>
    private static IntPtr MediaType(int rate)
    {
        int typeSize = Marshal.SizeOf<DmoMediaType>();
        int formatSize = Marshal.SizeOf<WaveFormatEx>();

        IntPtr block = Marshal.AllocHGlobal(typeSize + formatSize);
        IntPtr format = block + typeSize;

        var wave = new WaveFormatEx
        {
            FormatTag = WAVE_FORMAT_PCM,
            Channels = 1,
            SamplesPerSec = rate,
            BitsPerSample = 16,
            BlockAlign = 2,
            Size = 0,
        };
        wave.AvgBytesPerSec = wave.SamplesPerSec * wave.BlockAlign;

        Marshal.StructureToPtr(wave, format, false);

        var type = new DmoMediaType
        {
            MajorType = MediaTypeAudio,
            SubType = MediaSubTypePcm,
            FixedSizeSamples = 1,
            TemporalCompression = 0,
            SampleSize = wave.BlockAlign,
            FormatType = FormatWaveFormatEx,
            Unknown = IntPtr.Zero,
            FormatSize = formatSize,
            Format = format,
        };

        Marshal.StructureToPtr(type, block, false);
        return block;
    }

    private static void FreeMediaType(IntPtr type) => Marshal.FreeHGlobal(type);

    private const short VT_I4 = 3;
    private const short VT_BOOL = 11;
    private const short WAVE_FORMAT_PCM = 1;

    /// <summary>MFPKEY_WMAAECMA_SYSTEM_MODE.</summary>
    private static readonly PropertyKey SystemModeProperty =
        new(new Guid("6f52c567-0360-4bd2-9617-ccbf1421c939"), 2);

    /// <summary>MFPKEY_WMAAECMA_DMO_SOURCE_MODE, which must be set before anything else.</summary>
    private static readonly PropertyKey SourceModeProperty =
        new(new Guid("6f52c567-0360-4bd2-9617-ccbf1421c939"), 3);

    /// <summary>SINGLE_CHANNEL_AEC: one microphone, one reference, echo cancellation.</summary>
    private const int SingleChannelAec = 0;

    /// <summary>Ask whether a type would be accepted without accepting it.</summary>
    private const int DMO_SET_TYPEF_TEST_ONLY = 0x00000001;

    /// <summary>The buffer begins a frame, which is what a fixed-size PCM chunk is.</summary>
    private const int DMO_INPUT_DATA_BUFFERF_SYNCPOINT = 0x00000001;

    private static readonly Guid MediaTypeAudio = new("73647561-0000-0010-8000-00aa00389b71");
    private static readonly Guid MediaSubTypePcm = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid FormatWaveFormatEx = new("05589f81-c356-11ce-bf01-00aa0055595a");

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, int propertyId)
    {
        public Guid FormatId = formatId;
        public int PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public short Type;
        public short Reserved1;
        public short Reserved2;
        public short Reserved3;
        public IntPtr Value;
        public IntPtr Value2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx
    {
        public short FormatTag;
        public short Channels;
        public int SamplesPerSec;
        public int AvgBytesPerSec;
        public short BlockAlign;
        public short BitsPerSample;
        public short Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DmoMediaType
    {
        public Guid MajorType;
        public Guid SubType;
        public int FixedSizeSamples;
        public int TemporalCompression;
        public int SampleSize;
        public Guid FormatType;
        public IntPtr Unknown;
        public int FormatSize;
        public IntPtr Format;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DmoOutputDataBuffer
    {
        [MarshalAs(UnmanagedType.Interface)]
        public IMediaBuffer Buffer;

        public int Status;
        public long Timestamp;
        public long TimeLength;
    }

    /// <summary>
    /// A block of unmanaged memory the transform reads from and writes into.
    ///
    /// Managed, because the DMO wants an interface and the buffer's lifetime is this call's. The
    /// bytes themselves are unmanaged: the transform is handed the pointer and writes through it,
    /// which a pinned array would also allow and a moving one would not.
    /// </summary>
    private sealed class MediaBuffer(int capacity) : IMediaBuffer, IDisposable
    {
        private readonly IntPtr data = Marshal.AllocHGlobal(Math.Max(capacity, 1));
        private int length;
        private bool freed;

        /// <summary>Put bytes in and say how many, which is what a full buffer is.</summary>
        public void Fill(ReadOnlySpan<byte> bytes)
        {
            unsafe
            {
                bytes.CopyTo(new Span<byte>((void*)data, capacity));
            }

            length = bytes.Length;
        }

        /// <summary>Take whatever the transform left, up to the caller's room.</summary>
        public int Read(Span<byte> into)
        {
            int taken = Math.Min(length, into.Length);

            unsafe
            {
                new ReadOnlySpan<byte>((void*)data, taken).CopyTo(into);
            }

            return taken;
        }

        public int SetLength(int cbLength)
        {
            if (cbLength > capacity)
                return unchecked((int)0x80070057);

            length = cbLength;
            return 0;
        }

        public int GetMaxLength(out int cbMaxLength)
        {
            cbMaxLength = capacity;
            return 0;
        }

        public int GetBufferAndLength(out IntPtr ppBuffer, out int pcbLength)
        {
            ppBuffer = data;
            pcbLength = length;
            return 0;
        }

        public void Dispose()
        {
            if (freed)
                return;

            freed = true;
            Marshal.FreeHGlobal(data);
        }
    }

    [ComImport]
    [Guid("59eff8b9-938c-4a26-82f2-95cb84cdc837")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMediaBuffer
    {
        [PreserveSig]
        int SetLength(int cbLength);

        [PreserveSig]
        int GetMaxLength(out int cbMaxLength);

        [PreserveSig]
        int GetBufferAndLength(out IntPtr ppBuffer, out int pcbLength);
    }

    /// <summary>
    /// IMediaObject, whole.
    ///
    /// Every method in vtable order and none omitted, which is not tidiness: a COM interface is an
    /// offset table, so a missing method above one that is called sends the call to the wrong slot
    /// and the failure is a crash somewhere else entirely.
    /// </summary>
    [ComImport]
    [Guid("d8ad0f58-5494-4102-97c5-ec798e59bcf4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMediaObject
    {
        [PreserveSig]
        int GetStreamCount(out int inputs, out int outputs);

        [PreserveSig]
        int GetInputStreamInfo(int stream, out int flags);

        [PreserveSig]
        int GetOutputStreamInfo(int stream, out int flags);

        [PreserveSig]
        int GetInputType(int stream, int typeIndex, IntPtr type);

        [PreserveSig]
        int GetOutputType(int stream, int typeIndex, IntPtr type);

        [PreserveSig]
        int SetInputType(int stream, IntPtr type, int flags);

        [PreserveSig]
        int SetOutputType(int stream, IntPtr type, int flags);

        [PreserveSig]
        int GetInputCurrentType(int stream, IntPtr type);

        [PreserveSig]
        int GetOutputCurrentType(int stream, IntPtr type);

        [PreserveSig]
        int GetInputSizeInfo(int stream, out int size, out int maxLookahead, out int alignment);

        [PreserveSig]
        int GetOutputSizeInfo(int stream, out int size, out int alignment);

        [PreserveSig]
        int GetInputMaxLatency(int stream, out long latency);

        [PreserveSig]
        int SetInputMaxLatency(int stream, long latency);

        [PreserveSig]
        int Flush();

        [PreserveSig]
        int Discontinuity(int stream);

        [PreserveSig]
        int AllocateStreamingResources();

        [PreserveSig]
        int FreeStreamingResources();

        [PreserveSig]
        int GetInputStatus(int stream, out int flags);

        [PreserveSig]
        int ProcessInput(
            int stream,
            [MarshalAs(UnmanagedType.Interface)] IMediaBuffer buffer,
            int flags,
            long timestamp,
            long timeLength);

        [PreserveSig]
        int ProcessOutput(
            int flags,
            int outputBufferCount,
            [In, Out, MarshalAs(UnmanagedType.LPArray)] DmoOutputDataBuffer[] buffers,
            out int status);

        [PreserveSig]
        int Lock(int locked);
    }

    [ComImport]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out int count);

        [PreserveSig]
        int GetAt(int index, out PropertyKey key);

        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant value);

        [PreserveSig]
        int SetValue(ref PropertyKey key, ref PropVariant value);
    }
}

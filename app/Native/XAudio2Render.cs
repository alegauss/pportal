using System.Runtime.InteropServices;
using ChiakiNg.Session;

namespace ChiakiNg.Native;

/// <summary>
/// PP816: the speakers again, over the queue Windows already ships.
///
/// <see cref="WasapiRender"/> is a pump: it asks the engine how much room there is, works out how
/// much of a unit fits, and copies that much. PP810 is one line of that arithmetic going wrong -
/// the rest of the unit was dropped - and both of its measured fixes moved the loss rather than
/// ending it. There is no arithmetic here. A buffer is handed over whole and the engine plays it
/// after whatever is already queued, so the defect PP810 describes cannot be written.
///
/// WHAT REPLACES THE PUMP IS NOTHING. No poll interval, no room, no padding, no silence written to
/// keep an engine fed, no drain thread. The engine plays what it holds and reports how many buffers
/// are still outstanding, which is the one number a caller needs.
///
/// THE PCM OUTLIVES THE CALL AND THAT IS THE TRAP. SubmitSourceBuffer returns immediately and the
/// engine reads the bytes afterwards, so a managed array handed over has to stay alive AND stay put
/// until the engine is done with it. Every submitted buffer is pinned and held here, and freed only
/// once <see cref="XAudio2VoiceState.BuffersQueued"/> says the engine has let it go - which is the
/// callback interface not being needed. Unpinning early does not fail: it plays whatever the
/// collector moved into that address.
///
/// A MACHINE WITH NO ENGINE IS REPORTED AND NOT THROWN, which is <see cref="WasapiRender"/>'s rule
/// and PP708's before it: whether a machine can play sound is a fact about the machine, and a host
/// that cannot still has a session to run.
/// </summary>
public sealed class XAudio2Render : IDisposable
{
    private readonly MicrophoneAnnouncement format;
    private readonly Lock gate = new();
    private readonly Queue<GCHandle> submitted = new();

    private IntPtr engine;
    private IntPtr mastering;
    private IntPtr source;
    private long framesSubmitted;
    private bool disposed;

    /// <param name="announced">
    /// What the stream said it is. The source voice is created for exactly this, and the mastering
    /// voice takes the endpoint's own channels and rate - so the engine's own resampler sits
    /// between them, which is what AUTOCONVERTPCM did on the WASAPI side.
    /// </param>
    public XAudio2Render(MicrophoneAnnouncement? announced = null)
        => format = announced ?? new MicrophoneAnnouncement(2, 16, 48000, 480);

    /// <summary>The format this render asked the engine for.</summary>
    public MicrophoneAnnouncement Format => format;

    /// <summary>The last HRESULT a failed step returned, for a caller reading why.</summary>
    public int LastError { get; private set; }

    /// <summary>Whether the engine, the mastering voice and the source voice all exist.</summary>
    public bool Running => source != IntPtr.Zero;

    /// <summary>One frame of the announced format, in bytes.</summary>
    public int FrameBytes => format.Channels * MicrophoneFormat.BytesPerSample(format);

    /// <summary>How many frames have been handed to the engine, whether or not they have played.</summary>
    public long FramesSubmitted => Interlocked.Read(ref framesSubmitted);

    /// <summary>
    /// How many submitted buffers the engine has not finished, which is the queue depth.
    /// </summary>
    /// <remarks>
    /// The number WASAPI made <see cref="WasapiRender.QueuedBytes"/> derive by walking a queue. Here
    /// it is the engine's own answer, and it is what says whether the sound is keeping up.
    /// </remarks>
    public int Queued => Running ? (int)State().BuffersQueued : 0;

    /// <summary>Samples the engine has played since the voice started, which never resets.</summary>
    public ulong SamplesPlayed => Running ? State().SamplesPlayed : 0;

    /// <summary>
    /// Open the engine, a mastering voice on the default endpoint, and a source voice for the
    /// announced format.
    /// </summary>
    /// <returns>Running, or the reason it is not.</returns>
    public RenderResult Open()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (Running)
            return RenderResult.Running;

        if (!XAudio2.IsAvailable())
            return RenderResult.NoDevice;

        LastError = XAudio2.XAudio2Create(out engine, 0, XAudio2.Processor1);
        if (LastError != 0 || engine == IntPtr.Zero)
            return Refuse();

        LastError = XAudio2.CreateMasteringVoice(engine, out mastering);
        if (LastError != 0 || mastering == IntPtr.Zero)
        {
            // No endpoint is a machine with no speakers rather than a refusal, which is the one
            // failure here that is about the MACHINE and not about this code.
            Teardown();
            return RenderResult.NoDevice;
        }

        IntPtr wanted = Wasapi.Format(format);
        try
        {
            LastError = XAudio2.CreateSourceVoice(engine, wanted, out source);
        }
        finally
        {
            Marshal.FreeHGlobal(wanted);
        }

        if (LastError != 0 || source == IntPtr.Zero)
            return Refuse();

        LastError = XAudio2.StartEngine(engine);
        if (LastError != 0)
            return Refuse();

        // The voice plays what it is given from here on. Started before anything is queued, because
        // a voice started later would hold its first buffers until somebody remembered to.
        LastError = XAudio2.Start(source);

        return LastError == 0 ? RenderResult.Running : Refuse();
    }

    /// <summary>
    /// Hand one unit of PCM to the engine, whole.
    /// </summary>
    /// <remarks>
    /// There is no partial write and no return of how much was taken, which is the whole of what
    /// this line buys: the caller cannot be told that half of its audio was accepted, because that
    /// is not a thing the engine does.
    /// </remarks>
    /// <param name="unit">Interleaved PCM in the announced format.</param>
    public void Write(ReadOnlySpan<byte> unit)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (unit.IsEmpty || !Running)
            return;

        // Copied and PINNED, and released only when the engine says it is done with it. The copy is
        // this side's for the reason WasapiRender's is: a decoded frame is the audio path's own
        // buffer and is reused long before the engine would have got to it.
        GCHandle held = GCHandle.Alloc(unit.ToArray(), GCHandleType.Pinned);

        var buffer = new XAudio2Buffer
        {
            AudioBytes = (uint)unit.Length,
            AudioData = held.AddrOfPinnedObject(),
        };

        lock (gate)
        {
            int hr = XAudio2.SubmitSourceBuffer(source, ref buffer);

            if (hr != 0)
            {
                LastError = hr;
                held.Free();
                return;
            }

            submitted.Enqueue(held);
            Interlocked.Add(ref framesSubmitted, unit.Length / FrameBytes);

            ReleasePlayed();
        }
    }

    /// <summary>
    /// Drop everything the engine has not played, which is the sink being too far behind.
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="WasapiRender.Clear"/>, and the pins go with it - a flushed
    /// buffer is one the engine will never report as finished, so waiting for it would leak.
    /// </remarks>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (!Running)
            return;

        lock (gate)
        {
            XAudio2.FlushSourceBuffers(source);

            while (submitted.Count > 0)
                submitted.Dequeue().Free();
        }
    }

    /// <summary>
    /// The engine's own volume, which is what makes a scaling pass over every sample unnecessary.
    /// </summary>
    /// <param name="volume">
    /// One is unchanged. <see cref="AudioVolume.MaxVolume"/> is the port's 128, so a setting of that
    /// scale divides by it - and the engine does the multiply per sample instead of managed code.
    /// </param>
    public void SetVolume(float volume)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (Running)
            LastError = XAudio2.SetVolume(source, volume);
    }

    /// <summary>Stop the voice, release every pin, and let the engine go.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        Teardown();
    }

    private XAudio2VoiceState State()
    {
        lock (gate)
            return XAudio2.GetState(source);
    }

    /// <summary>
    /// Free the pins the engine has finished with, oldest first.
    /// </summary>
    /// <remarks>
    /// BuffersQueued is how many are still outstanding, and the queue here is in submit order, so
    /// everything beyond that count at the FRONT has been played. This is the callback interface
    /// not being needed: the engine already answers the only question a pin has.
    /// </remarks>
    private void ReleasePlayed()
    {
        var outstanding = (int)XAudio2.GetState(source).BuffersQueued;

        while (submitted.Count > outstanding)
            submitted.Dequeue().Free();
    }

    private RenderResult Refuse()
    {
        Teardown();
        return RenderResult.Refused;
    }

    private void Teardown()
    {
        lock (gate)
        {
            if (source != IntPtr.Zero)
            {
                XAudio2.Stop(source);
                XAudio2.FlushSourceBuffers(source);
                XAudio2.DestroyVoice(source);
                source = IntPtr.Zero;
            }

            // After the voices, because a pinned buffer the engine is still reading is exactly what
            // the destroy above is what stops.
            while (submitted.Count > 0)
                submitted.Dequeue().Free();

            if (mastering != IntPtr.Zero)
            {
                XAudio2.DestroyVoice(mastering);
                mastering = IntPtr.Zero;
            }

            if (engine != IntPtr.Zero)
            {
                XAudio2.StopEngine(engine);
                XAudio2.ReleaseEngine(engine);
                engine = IntPtr.Zero;
            }
        }
    }
}

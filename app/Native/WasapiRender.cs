using System.Runtime.InteropServices;
using ChiakiNg.Session;

namespace ChiakiNg.Native;

/// <summary>What opening a render device did.</summary>
public enum RenderResult
{
    /// <summary>Open, running, and playing whatever is written to it.</summary>
    Running,

    /// <summary>There is no active render endpoint, which is a machine with no speakers.</summary>
    NoDevice,

    /// <summary>WASAPI refused. <see cref="WasapiRender.LastError"/> holds the HRESULT.</summary>
    Refused,
}

/// <summary>
/// PP708: the speakers, opened - which is the sentence a stream with no sound existed to deny.
///
/// PP700 joined a decoder to the session and a stream decoded for the first time. Nothing joined a
/// speaker: there was no IAudioRenderClient anywhere in the assembly, and AudioRing - PP32's
/// playback buffer, with its capacity, drain target and clear threshold - had the selftest as its
/// only caller. The frames existed and stopped at a seam.
///
/// THE MIRROR OF PP652, deliberately and to the letter. The default endpoint, the announced format
/// through AUTOCONVERTPCM, a pump on its own thread, and the same reporting rather than throwing:
/// whether a machine has speakers is a fact about the machine, and a host that cannot open them
/// still has a session to run.
///
/// AND THE ROLE IS THE CONSOLE'S, not the communications one. PP698 made the same choice for the
/// loopback reference and for the same reason: what a person hears is on the endpoint the game's
/// audio is on, and the communications endpoint is where a voice path goes. Opening that one would
/// play a console's soundtrack into a headset's telephone channel.
///
/// WRITING IS A LEASE. GetBuffer hands back a pointer into the engine's own ring and ReleaseBuffer
/// says how much was filled, so a caller that takes a buffer and does not release it stalls the
/// engine. That is the opposite failure from the capture side's, where a missed release drops
/// audio - here it stops it - which is why every path below releases what it took.
///
/// SILENCE IS WRITTEN, NOT SKIPPED. A render client whose caller has nothing to play must still
/// fill the buffer, or the engine repeats whatever was there. The AUDCLNT_BUFFERFLAGS_SILENT flag
/// is how that is said without moving zeroes, and it is the same flag the capture side reads.
/// </summary>
public sealed class WasapiRender : IDisposable
{
    /// <summary>How much the engine buffers, in hundred-nanosecond units. A hundred milliseconds.</summary>
    public const long BufferDuration = WasapiCapture.BufferDuration;

    /// <summary>How long the pump waits before looking at the engine's padding again.</summary>
    public static TimeSpan PollInterval { get; } = TimeSpan.FromMilliseconds(5);

    private readonly MicrophoneAnnouncement format;
    private readonly Queue<byte[]> pending = new();
    private readonly Lock gate = new();
    private readonly CancellationTokenSource stopping = new();

    private Wasapi.IAudioClient? client;
    private Wasapi.IAudioRenderClient? render;
    private Thread? pump;
    private int bufferFrames;
    private long startedAt;
    private long framesWritten;
    private bool disposed;

    /// <param name="format">
    /// What to hand the engine, which defaults to the format the console announces.
    ///
    /// PP711 gave the capture the same choice for the same reason: AUTOCONVERTPCM puts a converter
    /// in front, so a caller writes what it has and Windows makes it fit the device.
    /// </param>
    public WasapiRender(MicrophoneAnnouncement? format = null)
        => this.format = format ?? MicrophoneFormat.Announced;

    /// <summary>The HRESULT of whatever last refused, or zero.</summary>
    public int LastError { get; private set; }

    /// <summary>The device's name, once one is open.</summary>
    public string DeviceName { get; private set; } = string.Empty;

    /// <summary>The format this render asked the engine for.</summary>
    public MicrophoneAnnouncement Format => format;

    /// <summary>How many frames have reached the engine, silence included.</summary>
    public long FramesWritten => Interlocked.Read(ref framesWritten);

    /// <summary>How many units are waiting to be played.</summary>
    public int Queued
    {
        get
        {
            lock (gate)
                return pending.Count;
        }
    }

    /// <summary>How long the pump has been running.</summary>
    public TimeSpan RunningFor
        => pump is null ? TimeSpan.Zero : System.Diagnostics.Stopwatch.GetElapsedTime(Volatile.Read(ref startedAt));

    /// <summary>
    /// PP695's judgement, on the other side: whether the endpoint is actually taking anything.
    ///
    /// The same reading the capture has and the same reason for it - a device that opened is not a
    /// device that is working. Here "streaming" means the engine has taken frames from this client,
    /// which it does as fast as it drains whether or not anybody is listening.
    /// </summary>
    public CaptureHealth Health => CaptureSilence.Judge(pump is not null, RunningFor, FramesWritten);

    /// <summary>Every active render endpoint, as an id and a name.</summary>
    public static IReadOnlyList<(string Id, string Name)> ActiveEndpoints()
        => WasapiCapture.ActiveRenderEndpoints();

    /// <summary>
    /// Open a render endpoint and start playing whatever is written to it.
    /// </summary>
    /// <param name="deviceId">One endpoint, or null for the default the console role names.</param>
    public RenderResult Start(string? deviceId = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (pump is not null)
            return RenderResult.Running;

        Wasapi.IMMDeviceEnumerator? enumerator = Wasapi.Enumerator(out int hresult);
        if (enumerator is null)
        {
            LastError = hresult;
            return RenderResult.Refused;
        }

        Wasapi.IMMDevice? device = null;

        try
        {
            LastError = deviceId is null
                ? enumerator.GetDefaultAudioEndpoint(
                    Wasapi.EDataFlow.Render, Wasapi.ERole.Console, out device)
                : enumerator.GetDevice(deviceId, out device);

            if (LastError != 0 || device is null)
                return RenderResult.NoDevice;

            DeviceName = Wasapi.NameOf(device);

            LastError = device.Activate(Wasapi.AudioClientIid, Wasapi.ClsCtxAll, IntPtr.Zero, out object? activated);
            if (LastError != 0 || activated is not Wasapi.IAudioClient opened)
                return RenderResult.Refused;

            client = opened;

            IntPtr wanted = Wasapi.Format(format);
            try
            {
                LastError = client.Initialize(
                    Wasapi.AudioClientShareMode.Shared,
                    WasapiCapture.AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM
                        | WasapiCapture.AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
                    BufferDuration,
                    0,
                    wanted,
                    IntPtr.Zero);
            }
            finally
            {
                Marshal.FreeHGlobal(wanted);
            }

            if (LastError != 0)
                return RenderResult.Refused;

            LastError = client.GetBufferSize(out bufferFrames);
            if (LastError != 0)
                return RenderResult.Refused;

            LastError = client.GetService(Wasapi.RenderClientIid, out object? service);
            if (LastError != 0 || service is not Wasapi.IAudioRenderClient writer)
                return RenderResult.Refused;

            render = writer;

            LastError = client.Start();
            if (LastError != 0)
                return RenderResult.Refused;

            Volatile.Write(ref startedAt, System.Diagnostics.Stopwatch.GetTimestamp());

            pump = new Thread(Pump)
            {
                IsBackground = true,
                Name = "audio render",
            };
            pump.Start();

            return RenderResult.Running;
        }
        finally
        {
            if (device is not null)
                Marshal.ReleaseComObject(device);

            Marshal.ReleaseComObject(enumerator);
        }
    }

    /// <summary>
    /// Hand over a unit to be played, copied because the engine takes it on another thread.
    /// </summary>
    /// <remarks>
    /// The copy is this side's and not the caller's, which is the opposite of the capture's
    /// contract and for the same reason: there the sink is called on WASAPI's thread and here the
    /// caller's bytes have to outlive the call. A decoded frame is the audio path's own buffer and
    /// is reused before the engine would have got to it.
    /// </remarks>
    public void Write(ReadOnlySpan<byte> unit)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (unit.IsEmpty)
            return;

        lock (gate)
            pending.Enqueue(unit.ToArray());
    }

    /// <summary>
    /// Fill whatever room the engine has, from the queue or with silence.
    ///
    /// Padding is what the engine has NOT played yet, so the room is the buffer less that - and a
    /// pass that asks for more than the room gets AUDCLNT_E_BUFFER_TOO_LARGE rather than a short
    /// write. Silence goes in where the queue is empty, because a render client that skips a pass
    /// leaves the engine repeating what was there.
    /// </summary>
    private void Pump()
    {
        int frameBytes = format.Channels * MicrophoneFormat.BytesPerSample(format);

        while (!stopping.IsCancellationRequested)
        {
            if (client is not { } engine || render is not { } writer)
                return;

            if (engine.GetCurrentPadding(out int padding) != 0)
                return;

            int room = bufferFrames - padding;
            if (room <= 0)
            {
                stopping.Token.WaitHandle.WaitOne(PollInterval);
                continue;
            }

            byte[]? unit = Next();
            int frames = unit is null ? room : Math.Min(room, unit.Length / frameBytes);

            if (frames <= 0)
            {
                stopping.Token.WaitHandle.WaitOne(PollInterval);
                continue;
            }

            if (writer.GetBuffer(frames, out IntPtr buffer) != 0)
                return;

            var flags = 0;

            if (unit is null || buffer == IntPtr.Zero)
            {
                // Nothing to play. The flag says so without moving a byte, which is the same one the
                // capture side reads on the way in.
                flags = Wasapi.BufferFlagsSilent;
            }
            else
            {
                unsafe
                {
                    unit.AsSpan(0, frames * frameBytes)
                        .CopyTo(new Span<byte>((void*)buffer, frames * frameBytes));
                }
            }

            if (writer.ReleaseBuffer(frames, flags) != 0)
                return;

            Interlocked.Add(ref framesWritten, frames);

            if (unit is null)
                stopping.Token.WaitHandle.WaitOne(PollInterval);
        }
    }

    private byte[]? Next()
    {
        lock (gate)
            return pending.Count > 0 ? pending.Dequeue() : null;
    }

    /// <summary>Stop playing and release the device.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        stopping.Cancel();

        // Bounded, the way the capture's is: a pump that will not finish must not take the host
        // down with it.
        pump?.Join(TimeSpan.FromSeconds(2));
        pump = null;

        if (client is not null)
        {
            client.Stop();
            Marshal.ReleaseComObject(client);
            client = null;
        }

        if (render is not null)
        {
            Marshal.ReleaseComObject(render);
            render = null;
        }

        stopping.Dispose();

        lock (gate)
            pending.Clear();
    }
}

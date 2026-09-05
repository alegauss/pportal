using System.Runtime.InteropServices;
using ChiakiNg.Session;

namespace ChiakiNg.Native;

/// <summary>Which side of the audio engine a capture reads.</summary>
public enum CaptureSide
{
    /// <summary>A capture endpoint: a microphone, which is what PP652 opened.</summary>
    Microphone,

    /// <summary>
    /// PP698: a RENDER endpoint, read back through the loopback flag - what the speakers are playing.
    ///
    /// The second input an echo canceller needs. It is the same IAudioClient and the same capture
    /// client underneath, so what differs is one flag, which endpoint is asked for, and what silence
    /// means: a loopback client on a render endpoint playing nothing produces NOTHING rather than
    /// zeroes, which is the state PP695 taught this port to notice.
    /// </summary>
    RenderLoopback,
}

/// <summary>What opening a capture device did.</summary>
public enum CaptureResult
{
    /// <summary>Open, running, and handing units to the sink.</summary>
    Running,

    /// <summary>There is no active capture endpoint, which is a machine with no microphone.</summary>
    NoDevice,

    /// <summary>WASAPI refused. <see cref="WasapiCapture.LastError"/> holds the HRESULT.</summary>
    Refused,
}

/// <summary>
/// PP652: the microphone, opened.
///
/// <see cref="MicrophoneSurface"/> counted four subsystems assuming a microphone and nothing
/// producing one sample. This is the producer. The default COMMUNICATIONS endpoint, because that is
/// the one Windows nominates for a voice path and the one a person expects a headset to be.
///
/// EVERY CONVERSION IS WINDOWS'S. PP652's spike measured that no capture device here takes the
/// announced one-channel 16-bit 48000 format in shared mode - a shared client gets the mix format,
/// and a mix format is 32-bit float, at 16000 Hz on this machine's default headset. But
/// AUTOCONVERTPCM initialises on every device, so the engine puts a converter in front and the
/// client reads exactly what the console was told about. A resample written here would be a second
/// one, and worse.
///
/// IsFormatSupported does not know about that flag, which is why the spike had to initialise rather
/// than ask, and why this does the same thing rather than checking first.
///
/// THE LOOP IS EVENT-DRIVEN AND THE SINK IS NOT. A capture callback runs on a thread WASAPI is
/// waiting on, so anything slow in the sink is a dropped packet. What this hands over is a span
/// valid for that call alone - <see cref="MicrophoneUnits"/>'s contract - and a caller that needs
/// the bytes to outlive it copies them.
///
/// Every COM method here carries PreserveSig, which PP693 made a check rather than a habit: without
/// it the CLR reads the declared int as a retval and every HRESULT test compares against an
/// uninitialised local.
/// </summary>
public sealed class WasapiCapture : IDisposable
{
    /// <summary>How much the engine buffers, in hundred-nanosecond units. A hundred milliseconds.</summary>
    public const long BufferDuration = 100 * 10_000L;

    /// <summary>How long the loop waits for a packet before looking again.</summary>
    public static TimeSpan PollInterval { get; } = TimeSpan.FromMilliseconds(5);

    private readonly Action<ReadOnlySpan<byte>> sink;
    private readonly MicrophoneAnnouncement format;
    private readonly MicrophoneUnits units;
    private readonly CancellationTokenSource stopping = new();

    private IAudioClient? client;
    private IAudioCaptureClient? capture;
    private Thread? pump;
    private bool disposed;

    /// <summary>The HRESULT of whatever last refused, or zero.</summary>
    public int LastError { get; private set; }

    /// <summary>How many whole units have reached the sink.</summary>
    public long UnitsCaptured => units.Emitted;

    /// <summary>The device's name, once one is open.</summary>
    public string DeviceName { get; private set; } = string.Empty;

    /// <summary>Which side of the engine this one reads. Set by the Start that opened it.</summary>
    public CaptureSide Side { get; private set; } = CaptureSide.Microphone;

    /// <summary>When the pump started, which is what makes silence measurable.</summary>
    private long startedAt;

    /// <summary>How long the capture has been running.</summary>
    public TimeSpan RunningFor
        => pump is null ? TimeSpan.Zero : System.Diagnostics.Stopwatch.GetElapsedTime(Volatile.Read(ref startedAt));

    /// <summary>
    /// PP695: whether the endpoint is actually speaking, which is not whether it opened.
    ///
    /// A Bluetooth headset in its music profile has no microphone, and opening the capture endpoint
    /// is only meant to make Windows switch. When it does not, everything here reports success and
    /// no audio arrives - so this is the reading a host acts on.
    /// </summary>
    public CaptureHealth Health => CaptureSilence.Judge(pump is not null, RunningFor, UnitsCaptured);

    /// <summary>A capture that hands whole units to <paramref name="sink"/>.</summary>
    /// <param name="format">
    /// PP711: what to ask the engine for, which defaults to what the console was told.
    ///
    /// AUTOCONVERTPCM is what makes this a choice at all - the engine puts a converter in front and
    /// the client reads exactly what it asked for, whatever the device runs at. So a caller that
    /// wants the echo canceller's rate asks for it here and the two downward conversions PP52's
    /// stage pays for happen inside Windows instead, for no code and no copy.
    ///
    /// The unit follows the format. A unit is ten milliseconds either way, so asking for 16000
    /// makes it 320 bytes rather than 960 - and a sink sized from the announced format would be
    /// reading three units as one.
    /// </param>
    public WasapiCapture(Action<ReadOnlySpan<byte>> sink, MicrophoneAnnouncement? format = null)
    {
        ArgumentNullException.ThrowIfNull(sink);

        this.sink = sink;
        this.format = format ?? MicrophoneFormat.Announced;
        units = new MicrophoneUnits(MicrophoneFormat.BytesPerUnit(this.format));
    }

    /// <summary>The format this capture asked the engine for.</summary>
    public MicrophoneAnnouncement Format => format;

    /// <summary>
    /// Every active capture endpoint, as an id and a name.
    ///
    /// PP652: A HOST HAS TO OFFER THIS, and a check has to be able to use it. The default
    /// communications endpoint here is a Bluetooth headset, and a Bluetooth headset that is
    /// connected is not one that is streaming - it sits in a music profile with no microphone until
    /// something makes it switch, and the switch does not always happen. So "the default endpoint"
    /// is a reasonable first choice and a bad only choice.
    /// </summary>
    public static IReadOnlyList<(string Id, string Name)> ActiveCaptureEndpoints()
        => ActiveEndpoints(EDataFlow.Capture);

    /// <summary>
    /// PP698: every active RENDER endpoint, which is where a loopback reference comes from.
    ///
    /// The same enumeration with the flow reversed. A host offering a reference device has the same
    /// reason to offer a choice that PP652 had on the capture side: the default is a reasonable
    /// first answer and a bad only one, and here it is also the one a person can change by plugging
    /// in headphones mid-session.
    /// </summary>
    public static IReadOnlyList<(string Id, string Name)> ActiveRenderEndpoints()
        => ActiveEndpoints(EDataFlow.Render);

    private static IReadOnlyList<(string Id, string Name)> ActiveEndpoints(EDataFlow flow)
    {
        object enumeratorObject;
        try
        {
            enumeratorObject = Activator.CreateInstance(Type.GetTypeFromCLSID(MMDeviceEnumeratorClsid)!)!;
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or NotSupportedException)
        {
            return [];
        }

        var enumerator = (IMMDeviceEnumerator)enumeratorObject;

        try
        {
            if (enumerator.EnumAudioEndpoints(flow, DeviceStateActive, out IMMDeviceCollection? all) != 0
                || all is null)
            {
                return [];
            }

            try
            {
                if (all.GetCount(out int count) != 0)
                    return [];

                var found = new List<(string, string)>();

                for (int i = 0; i < count; i++)
                {
                    if (all.Item(i, out IMMDevice? device) != 0 || device is null)
                        continue;

                    try
                    {
                        if (device.GetId(out string id) == 0)
                            found.Add((id, NameOf(device)));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }

                return found;
            }
            finally
            {
                Marshal.ReleaseComObject(all);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    /// <summary>
    /// Open a capture endpoint and start reading.
    ///
    /// With no id, the default communications endpoint, which is the one Windows nominates for a
    /// voice path and the one a person expects a headset to be. With an id, that endpoint, which is
    /// what a setting would pass and what a check uses when the default will not stream.
    ///
    /// Reports rather than throws, for <see cref="Session.SurfacePresenter"/>'s reason: whether a
    /// machine has a microphone is a fact about the machine, and a host that cannot start one still
    /// has a session to run.
    /// </summary>
    public CaptureResult Start(string? deviceId = null) => Start(CaptureSide.Microphone, deviceId);

    /// <summary>
    /// PP698: the same, on either side of the engine.
    ///
    /// <see cref="CaptureSide.RenderLoopback"/> asks for a RENDER endpoint and adds one flag, and
    /// everything below is unchanged: the same client, the same announced format through the same
    /// converter, the same unit accumulator, the same pump. That is why this is an argument rather
    /// than a second class - a loopback reference that split the interop would be two copies of a
    /// COM surface PP693 already had to make a rule about.
    ///
    /// THE ROLE DIFFERS AND IS NOT AN OVERSIGHT. A microphone is opened on the COMMUNICATIONS
    /// endpoint, because that is the one Windows nominates for a voice path. A reference is what the
    /// person actually HEARS, which is the console role - the endpoint the game's own audio is on.
    /// Opening the communications render endpoint would give a reference of a device that may be
    /// playing nothing while the speakers play the stream.
    /// </summary>
    public CaptureResult Start(CaptureSide side, string? deviceId = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (pump is not null)
            return CaptureResult.Running;

        Side = side;

        object enumeratorObject;
        try
        {
            enumeratorObject = Activator.CreateInstance(Type.GetTypeFromCLSID(MMDeviceEnumeratorClsid)!)!;
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or NotSupportedException)
        {
            LastError = error.HResult;
            return CaptureResult.Refused;
        }

        var enumerator = (IMMDeviceEnumerator)enumeratorObject;
        IMMDevice? device = null;

        try
        {
            LastError = deviceId is null
                ? enumerator.GetDefaultAudioEndpoint(FlowOf(side), RoleOf(side), out device)
                : enumerator.GetDevice(deviceId, out device);

            if (LastError != 0 || device is null)
                return CaptureResult.NoDevice;

            DeviceName = NameOf(device);

            LastError = device.Activate(AudioClientIid, CLSCTX_ALL, IntPtr.Zero, out object? activated);
            if (LastError != 0 || activated is not IAudioClient opened)
                return CaptureResult.Refused;

            client = opened;

            IntPtr format = Announced();
            try
            {
                LastError = client.Initialize(
                    AudioClientShareMode.Shared,
                    FlagsFor(side),
                    BufferDuration,
                    0,
                    format,
                    IntPtr.Zero);
            }
            finally
            {
                Marshal.FreeHGlobal(format);
            }

            if (LastError != 0)
                return CaptureResult.Refused;

            LastError = client.GetService(CaptureClientIid, out object? service);
            if (LastError != 0 || service is not IAudioCaptureClient reader)
                return CaptureResult.Refused;

            capture = reader;

            LastError = client.Start();
            if (LastError != 0)
                return CaptureResult.Refused;

            Volatile.Write(ref startedAt, System.Diagnostics.Stopwatch.GetTimestamp());

            pump = new Thread(Pump)
            {
                IsBackground = true,
                Name = "microphone capture",
            };
            pump.Start();

            return CaptureResult.Running;
        }
        finally
        {
            if (device is not null)
                Marshal.ReleaseComObject(device);

            Marshal.ReleaseComObject(enumerator);
        }
    }

    /// <summary>
    /// Read packets until stopped, handing whole units on.
    ///
    /// A silent device returns zero-sized packets rather than nothing, and the SILENT flag means
    /// the buffer pointer is not to be read - so a run of silence is fed as zeroes, which keeps the
    /// unit clock running rather than letting the encoder drift through a quiet moment.
    /// </summary>
    /// <remarks>
    /// PP698: a LOOPBACK client on a render endpoint playing nothing is a different silence. It
    /// returns no packets at all rather than zero-filled ones, so this loop polls and the unit clock
    /// stops - which is exactly the state <see cref="CaptureSilence"/> judges, and the reason the
    /// reference side needs no special case here.
    /// </remarks>
    private void Pump()
    {
        byte[] silence = new byte[units.UnitBytes];

        while (!stopping.IsCancellationRequested)
        {
            if (capture is not { } reader)
                return;

            if (reader.GetNextPacketSize(out int frames) != 0)
                return;

            if (frames == 0)
            {
                stopping.Token.WaitHandle.WaitOne(PollInterval);
                continue;
            }

            if (reader.GetBuffer(out IntPtr buffer, out int got, out int flags, out _, out _) != 0)
                return;

            try
            {
                int bytes = got * units.UnitBytes / format.FrameSize;

                if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0 || buffer == IntPtr.Zero)
                {
                    for (int at = 0; at < bytes; at += silence.Length)
                        units.Take(silence.AsSpan(0, Math.Min(silence.Length, bytes - at)), sink);
                }
                else
                {
                    unsafe
                    {
                        units.Take(new ReadOnlySpan<byte>((void*)buffer, bytes), sink);
                    }
                }
            }
            finally
            {
                reader.ReleaseBuffer(got);
            }
        }
    }

    /// <summary>Stop reading and release the device.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        stopping.Cancel();

        // Bounded: a pump that will not finish must not take the host down with it, which is the
        // rule PP618's runner exists for on the other side of the process.
        pump?.Join(TimeSpan.FromSeconds(2));
        pump = null;

        if (client is not null)
        {
            client.Stop();
            Marshal.ReleaseComObject(client);
            client = null;
        }

        if (capture is not null)
        {
            Marshal.ReleaseComObject(capture);
            capture = null;
        }

        stopping.Dispose();
        units.Reset();
    }

    private static string NameOf(IMMDevice device)
    {
        if (device.OpenPropertyStore(STGM_READ, out IPropertyStore? store) != 0 || store is null)
            return string.Empty;

        try
        {
            if (store.GetValue(ref FriendlyName, out PropVariant value) != 0)
                return string.Empty;

            string name = value.AsString() ?? string.Empty;
            PropVariantClear(ref value);
            return name;
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    /// <summary>The format this capture asked for, allocated unmanaged for a call taking a pointer.</summary>
    private IntPtr Announced()
    {
        MicrophoneAnnouncement announced = format;

        var wanted = new WaveFormatEx
        {
            FormatTag = WAVE_FORMAT_PCM,
            Channels = (short)announced.Channels,
            SamplesPerSec = announced.Rate,
            BitsPerSample = (short)announced.Bits,
            BlockAlign = (short)(announced.Channels * announced.Bits / 8),
            Size = 0,
        };
        wanted.AvgBytesPerSec = wanted.SamplesPerSec * wanted.BlockAlign;

        IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
        Marshal.StructureToPtr(wanted, buffer, false);
        return buffer;
    }

    private const int CLSCTX_ALL = 23;
    private const int STGM_READ = 0;
    private const short WAVE_FORMAT_PCM = 1;
    private const int AUDCLNT_BUFFERFLAGS_SILENT = 2;

    /// <summary>Put a converter in front of the engine, which is what makes the announced format reachable.</summary>
    public const int AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = unchecked((int)0x80000000);

    /// <summary>And let it resample, which the default endpoint at 16000 Hz needs.</summary>
    public const int AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000;

    /// <summary>
    /// PP698: read a RENDER endpoint back, which is the whole of what a reference stream is.
    ///
    /// The one flag that separates the two sides. Without it, activating an audio client on a render
    /// endpoint and asking for a capture service fails - a render endpoint has no capture service to
    /// give, and this is what makes it produce one.
    /// </summary>
    public const int AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;

    /// <summary>Which endpoints a side is looking at.</summary>
    private static EDataFlow FlowOf(CaptureSide side)
        => side == CaptureSide.RenderLoopback ? EDataFlow.Render : EDataFlow.Capture;

    /// <summary>
    /// And which default it wants: the voice endpoint for a microphone, the ordinary one for a
    /// reference of what is being heard.
    /// </summary>
    private static ERole RoleOf(CaptureSide side)
        => side == CaptureSide.RenderLoopback ? ERole.Console : ERole.Communications;

    /// <summary>
    /// The initialise flags, which are the capture's two plus loopback on the render side.
    ///
    /// AUTOCONVERTPCM is kept for the reason PP652 found it: the announced format is not what a
    /// shared client is given, and a render endpoint's mix format is 32-bit float at whatever rate
    /// the device runs. Keeping it here is what makes the reference arrive in the SAME units as the
    /// microphone, which is the only shape a subtraction can use.
    /// </summary>
    private static int FlagsFor(CaptureSide side)
    {
        int flags = AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY;

        return side == CaptureSide.RenderLoopback ? flags | AUDCLNT_STREAMFLAGS_LOOPBACK : flags;
    }

    private static readonly Guid MMDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid AudioClientIid = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    private static readonly Guid CaptureClientIid = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");

    private static PropertyKey FriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    private enum EDataFlow
    {
        Render = 0,
        Capture = 1,
    }

    private enum ERole
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2,
    }

    private enum AudioClientShareMode
    {
        Shared = 0,
        Exclusive = 1,
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

        /// <summary>VT_LPWSTR is 31, and a friendly name is one.</summary>
        public readonly string? AsString() => Type == 31 ? Marshal.PtrToStringUni(Value) : null;
    }

    /// <summary>DEVICE_STATE_ACTIVE, which is what "this endpoint is plugged in and enabled" means.</summary>
    private const int DeviceStateActive = 1;

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow flow, int stateMask, out IMMDeviceCollection? devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice? device);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice? device);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out int count);

        [PreserveSig]
        int Item(int index, out IMMDevice? device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(
            [MarshalAs(UnmanagedType.LPStruct)] Guid iid,
            int context,
            IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object? instance);

        [PreserveSig]
        int OpenPropertyStore(int access, out IPropertyStore? store);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
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
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig]
        int Initialize(
            AudioClientShareMode mode, int flags, long duration, long period, IntPtr format, IntPtr session);

        [PreserveSig]
        int GetBufferSize(out int frames);

        [PreserveSig]
        int GetStreamLatency(out long latency);

        [PreserveSig]
        int GetCurrentPadding(out int frames);

        [PreserveSig]
        int IsFormatSupported(AudioClientShareMode mode, IntPtr format, out IntPtr closest);

        [PreserveSig]
        int GetMixFormat(out IntPtr format);

        [PreserveSig]
        int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);

        [PreserveSig]
        int Start();

        [PreserveSig]
        int Stop();

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int SetEventHandle(IntPtr handle);

        [PreserveSig]
        int GetService([MarshalAs(UnmanagedType.LPStruct)] Guid iid,
            [MarshalAs(UnmanagedType.IUnknown)] out object? service);
    }

    [ComImport]
    [Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig]
        int GetBuffer(
            out IntPtr buffer,
            out int frames,
            out int flags,
            out long devicePosition,
            out long counterPosition);

        [PreserveSig]
        int ReleaseBuffer(int frames);

        [PreserveSig]
        int GetNextPacketSize(out int frames);
    }
}

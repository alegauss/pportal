using System.Runtime.InteropServices;

namespace ChiakiNg.Native;

/// <summary>XAUDIO2_BUFFER: a whole block of PCM handed to the engine and played entire.</summary>
/// <remarks>
/// The field order is the header's and the struct is passed by pointer, so a reordering here is a
/// buffer the engine reads at the wrong offsets rather than a compile error.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct XAudio2Buffer
{
    /// <summary>XAUDIO2_END_OF_STREAM, or zero. A stream that continues sets nothing.</summary>
    public uint Flags;

    /// <summary>How many bytes <see cref="AudioData"/> holds.</summary>
    public uint AudioBytes;

    /// <summary>The PCM. Read ASYNCHRONOUSLY, so it must outlive the submit.</summary>
    public IntPtr AudioData;

    /// <summary>First sample to play. Zero plays from the start.</summary>
    public uint PlayBegin;

    /// <summary>How many to play. Zero plays the whole buffer, which is what this port wants.</summary>
    public uint PlayLength;

    /// <summary>Loop start, unused here.</summary>
    public uint LoopBegin;

    /// <summary>Loop length, unused here.</summary>
    public uint LoopLength;

    /// <summary>Loop count, unused here.</summary>
    public uint LoopCount;

    /// <summary>A caller's cookie, returned by a callback this port does not register.</summary>
    public IntPtr Context;
}

/// <summary>XAUDIO2_VOICE_STATE: what the engine still holds, which is the queue depth for free.</summary>
/// <remarks>
/// PACKED, because XAudio2.h packs its structures to one byte and this is the struct where that
/// shows. Under natural alignment <see cref="SamplesPlayed"/> lands at offset 16 and the engine
/// writes it at 12, so the field reads as zero out of the caller's own padding - which is what it
/// did here while <see cref="BuffersQueued"/>, at 8 either way, was reporting a queue draining at
/// exactly real time. A wrong count that is always zero looks like a feature that does not work.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct XAudio2VoiceState
{
    /// <summary>The context of the buffer being played, which this port never sets.</summary>
    public IntPtr CurrentBufferContext;

    /// <summary>How many submitted buffers have not finished. The number WASAPI made us derive.</summary>
    public uint BuffersQueued;

    /// <summary>Samples played since the voice started, which never resets.</summary>
    public ulong SamplesPlayed;
}

/// <summary>
/// XAUDIO2_VOICE_DETAILS: what a voice was created as, which for a mastering voice is the
/// endpoint's own channel count.
/// </summary>
/// <remarks>
/// PP851 reads it because an output matrix has to be sized to the voice it feeds, and a mastering
/// voice opened with the default channels takes whatever the endpoint has - which is only known
/// after the open.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct XAudio2VoiceDetails
{
    /// <summary>The flags the voice was created with.</summary>
    public uint CreationFlags;

    /// <summary>The flags currently in force.</summary>
    public uint ActiveFlags;

    /// <summary>How many channels the voice takes in.</summary>
    public uint InputChannels;

    /// <summary>And at what rate.</summary>
    public uint InputSampleRate;
}

/// <summary>
/// PP816: XAudio2, reached by its vtable - the queue Windows already ships.
///
/// WASAPI hands out a buffer and asks how much of it to fill, so <see cref="WasapiRender"/> carries
/// the arithmetic: how much room, how much of a unit fits, what becomes of the rest. PP810 is one
/// line of that going wrong and it was audible. XAudio2 takes a WHOLE buffer and plays queued
/// buffers back to back, so there is no room to ask about and no tail to lose.
///
/// EVERY CALL IS A VTABLE INDEX AND NOT A ComImport INTERFACE, which is a departure from
/// <see cref="Wasapi"/> beside it and is forced rather than chosen. IXAudio2 does derive from
/// IUnknown and could be declared that way; IXAudio2Voice DOES NOT - it has no QueryInterface,
/// AddRef or Release at all - so the source and mastering voices cannot be ComImport interfaces in
/// any form. Rather than one file in two idioms, both ends are dispatched the same way and every
/// index below is named against the header's own order.
///
/// THE INDICES ARE THE WHOLE RISK. A wrong one calls a different method with this one's arguments,
/// which does not fail to compile and does not reliably crash. They are written as constants with
/// the header's declaration order beside them so a reader can count rather than trust.
///
/// AND THE PCM IS READ ASYNCHRONOUSLY. <see cref="XAudio2Buffer.AudioData"/> is still being read
/// after the submit returns, so a caller handing over a managed array has to keep it alive and
/// pinned until the engine says it is done - which <see cref="XAudio2VoiceState.BuffersQueued"/>
/// answers without a callback. <see cref="XAudio2Render"/> is where that is done.
/// </summary>
internal static class XAudio2
{
    /// <summary>The in-box engine, which every Windows 10 and later carries.</summary>
    public const string Library = "XAudio2_9.dll";

    /// <summary>
    /// XAUDIO2_PROCESSOR1, and NOT the zero that newer headers call XAUDIO2_USE_DEFAULT_PROCESSOR.
    /// </summary>
    /// <remarks>
    /// The two spellings are not interchangeable across versions and the difference is silent: with
    /// zero on this machine the engine was created, the source voice accepted eight buffers, and
    /// SamplesPlayed stayed at zero - a create that reported success and processed nothing. One is
    /// the mask every version has accepted since 2.7.
    /// </remarks>
    public const uint Processor1 = 1;

    /// <summary>XAUDIO2_DEFAULT_CHANNELS: let the mastering voice take the endpoint's own count.</summary>
    public const uint DefaultChannels = 0;

    /// <summary>XAUDIO2_DEFAULT_SAMPLERATE: and its own rate.</summary>
    public const uint DefaultSampleRate = 0;

    /// <summary>XAUDIO2_DEFAULT_FREQ_RATIO. Nothing here resamples, but the argument is required.</summary>
    public const float DefaultFrequencyRatio = 2.0f;

    /// <summary>XAUDIO2_COMMIT_NOW: no deferred operation set, which this port never uses.</summary>
    public const uint CommitNow = 0;

    // IXAudio2 derives from IUnknown, so its own methods start after QueryInterface, AddRef and
    // Release. In the header's order: RegisterForCallbacks, UnregisterForCallbacks,
    // CreateSourceVoice, CreateSubmixVoice, CreateMasteringVoice, StartEngine, StopEngine,
    // CommitChanges, GetPerformanceData, SetDebugConfiguration. UnregisterForCallbacks is the one a
    // reader skips - it returns void and is easy to miss - and skipping it moves every index below.
    private const int EngineRelease = 2;
    private const int EngineCreateSourceVoice = 5;
    private const int EngineCreateMasteringVoice = 7;
    private const int EngineStartEngine = 8;
    private const int EngineStopEngine = 9;

    // IXAudio2Voice has NO IUnknown, so its first method is at zero: GetVoiceDetails,
    // SetOutputVoices, SetEffectChain, EnableEffect, DisableEffect, GetEffectState,
    // SetEffectParameters, GetEffectParameters, SetFilterParameters, GetFilterParameters,
    // SetOutputFilterParameters, GetOutputFilterParameters, SetVolume, GetVolume,
    // SetChannelVolumes, GetChannelVolumes, SetOutputMatrix, GetOutputMatrix, DestroyVoice.
    private const int VoiceGetVoiceDetails = 0;
    private const int VoiceSetVolume = 12;
    private const int VoiceSetOutputMatrix = 16;
    private const int VoiceDestroyVoice = 18;

    // IXAudio2SourceVoice continues that table: Start, Stop, SubmitSourceBuffer,
    // FlushSourceBuffers, Discontinuity, ExitLoop, GetState, SetFrequencyRatio, GetFrequencyRatio,
    // SetSourceSampleRate.
    private const int SourceStart = 19;
    private const int SourceStop = 20;
    private const int SourceSubmitBuffer = 21;
    private const int SourceFlushBuffers = 22;
    private const int SourceGetState = 25;

    /// <summary>
    /// XAudio2Create, which is the one exported function the library has.
    /// </summary>
    /// <remarks>
    /// SYSTEM32 IS SPELLED OUT, and it has to be. <see cref="ChiakiNative"/> calls
    /// SetDefaultDllDirectories and AddDllDirectory to pin where the shim is loaded from, which
    /// changes the search for the whole process - so an in-box library that would be found by any
    /// ordinary load is subject to that decision too. Naming the directory here makes this call
    /// independent of it, which is what a system DLL should be.
    /// </remarks>
    [DllImport(Library, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int XAudio2Create(out IntPtr engine, uint flags, uint processor);

    /// <summary>Whether the engine's library is present, which every supported Windows makes true.</summary>
    /// <remarks>
    /// Asked rather than assumed for the reason <see cref="WasapiRender"/> reports instead of
    /// throwing: whether a machine can play sound is a fact about the machine, and a host that
    /// cannot still has a session to run.
    ///
    /// AND ASKED WITH THE SAME SEARCH THE CALL ABOVE USES. A bare TryLoad answers about the
    /// process's current directory list rather than about the machine, so this reported the library
    /// missing inside the application and present in the tests - on a machine where System32 holds
    /// it either way. A run then decoded 3607 frames and played none of them.
    /// </remarks>
    public static bool IsAvailable()
    {
        try
        {
            return NativeLibrary.TryLoad(
                Library, typeof(XAudio2).Assembly, DllImportSearchPath.System32, out nint handle)
                && handle != 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    /// <summary>IXAudio2::CreateMasteringVoice, taking the endpoint's own channels and rate.</summary>
    /// <param name="endpoint">
    /// PP851: an endpoint id as IMMDevice::GetId spells it, or null for the default. The haptics
    /// path is the caller that names one, because its endpoint IS the pad and never the default.
    /// </param>
    public static unsafe int CreateMasteringVoice(IntPtr engine, out IntPtr voice, string? endpoint = null)
    {
        IntPtr id = endpoint is null ? IntPtr.Zero : Marshal.StringToHGlobalUni(endpoint);

        try
        {
            fixed (IntPtr* into = &voice)
            {
                return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, uint, uint, uint, IntPtr, IntPtr, int, int>)
                    Method(engine, EngineCreateMasteringVoice))(
                        engine, into, DefaultChannels, DefaultSampleRate, 0, id, IntPtr.Zero, 0);
            }
        }
        finally
        {
            if (id != IntPtr.Zero)
                Marshal.FreeHGlobal(id);
        }
    }

    /// <summary>IXAudio2Voice::GetVoiceDetails, which returns nothing and fills the struct.</summary>
    public static unsafe XAudio2VoiceDetails GetVoiceDetails(IntPtr voice)
    {
        XAudio2VoiceDetails details = default;

        ((delegate* unmanaged[Stdcall]<IntPtr, XAudio2VoiceDetails*, void>)
            Method(voice, VoiceGetVoiceDetails))(voice, &details);

        return details;
    }

    /// <summary>
    /// IXAudio2Voice::SetOutputMatrix toward the voice's only destination.
    /// </summary>
    /// <param name="levels">
    /// Destination-major: the level of source channel S in destination channel D is at
    /// <c>levels[sourceChannels * D + S]</c>. Its length must be the product of the two counts.
    /// </param>
    public static unsafe int SetOutputMatrix(IntPtr voice, uint sourceChannels, uint destinationChannels, float[] levels)
    {
        fixed (float* matrix = levels)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, float*, uint, int>)
                Method(voice, VoiceSetOutputMatrix))(
                    voice, IntPtr.Zero, sourceChannels, destinationChannels, matrix, CommitNow);
        }
    }

    /// <summary>IXAudio2::CreateSourceVoice for one announced format.</summary>
    /// <param name="format">A WAVEFORMATEX, which <see cref="Wasapi.Format"/> already builds.</param>
    public static unsafe int CreateSourceVoice(IntPtr engine, IntPtr format, out IntPtr voice)
    {
        fixed (IntPtr* into = &voice)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, IntPtr, uint, float, IntPtr, IntPtr, IntPtr, int>)
                Method(engine, EngineCreateSourceVoice))(
                    engine, into, format, 0, DefaultFrequencyRatio, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }
    }

    /// <summary>IXAudio2::StartEngine.</summary>
    public static unsafe int StartEngine(IntPtr engine)
        => ((delegate* unmanaged[Stdcall]<IntPtr, int>)Method(engine, EngineStartEngine))(engine);

    /// <summary>IXAudio2::StopEngine, which returns nothing.</summary>
    public static unsafe void StopEngine(IntPtr engine)
        => ((delegate* unmanaged[Stdcall]<IntPtr, void>)Method(engine, EngineStopEngine))(engine);

    /// <summary>IUnknown::Release on the engine, which is the only reference this port takes.</summary>
    public static unsafe uint ReleaseEngine(IntPtr engine)
        => ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Method(engine, EngineRelease))(engine);

    /// <summary>IXAudio2SourceVoice::Start.</summary>
    public static unsafe int Start(IntPtr voice)
        => ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, int>)Method(voice, SourceStart))(
            voice, 0, CommitNow);

    /// <summary>IXAudio2SourceVoice::Stop.</summary>
    public static unsafe int Stop(IntPtr voice)
        => ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, int>)Method(voice, SourceStop))(
            voice, 0, CommitNow);

    /// <summary>
    /// IXAudio2SourceVoice::SubmitSourceBuffer - the call this whole line is for.
    /// </summary>
    /// <remarks>
    /// The second argument is the WMA buffer, which is null for PCM. There is no room parameter and
    /// no return of how much was taken: the engine takes the buffer whole and plays it after
    /// whatever is already queued.
    /// </remarks>
    public static unsafe int SubmitSourceBuffer(IntPtr voice, ref XAudio2Buffer buffer)
    {
        fixed (XAudio2Buffer* one = &buffer)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, XAudio2Buffer*, IntPtr, int>)
                Method(voice, SourceSubmitBuffer))(voice, one, IntPtr.Zero);
        }
    }

    /// <summary>IXAudio2SourceVoice::FlushSourceBuffers, which drops everything not yet played.</summary>
    public static unsafe int FlushSourceBuffers(IntPtr voice)
        => ((delegate* unmanaged[Stdcall]<IntPtr, int>)Method(voice, SourceFlushBuffers))(voice);

    /// <summary>IXAudio2SourceVoice::GetState, which returns nothing and fills the struct.</summary>
    public static unsafe XAudio2VoiceState GetState(IntPtr voice)
    {
        XAudio2VoiceState state = default;

        ((delegate* unmanaged[Stdcall]<IntPtr, XAudio2VoiceState*, uint, void>)
            Method(voice, SourceGetState))(voice, &state, 0);

        return state;
    }

    /// <summary>IXAudio2Voice::SetVolume, which is what makes AudioVolume's scaling the engine's.</summary>
    public static unsafe int SetVolume(IntPtr voice, float volume)
        => ((delegate* unmanaged[Stdcall]<IntPtr, float, uint, int>)Method(voice, VoiceSetVolume))(
            voice, volume, CommitNow);

    /// <summary>IXAudio2Voice::DestroyVoice, which returns nothing and must not be called twice.</summary>
    public static unsafe void DestroyVoice(IntPtr voice)
        => ((delegate* unmanaged[Stdcall]<IntPtr, void>)Method(voice, VoiceDestroyVoice))(voice);

    /// <summary>
    /// The function at one index of an object's vtable.
    /// </summary>
    /// <remarks>
    /// Two dereferences and they are easy to write as one: the object's first field is a pointer TO
    /// the table, and the table's entries are the functions. Getting that wrong reads the first
    /// function pointer as the table and calls whatever is at that address.
    /// </remarks>
    private static unsafe void* Method(IntPtr instance, int index)
        => (*(void***)instance)[index];
}

using System.Runtime.InteropServices;

namespace ChiakiNg.Native;

/// <summary>PP840: which step of the render-side ducking opt-out stopped it, or Ok.</summary>
public enum DuckingStep
{
    /// <summary>Nothing asked, which is every run with no microphone.</summary>
    NotAsked,

    /// <summary>Accepted: the system will not quiet this process's playback for its own capture.</summary>
    Ok,

    /// <summary>COM would not make a device enumerator.</summary>
    NoEnumerator,

    /// <summary>There is no default render endpoint, which is a machine with no speakers.</summary>
    NoDevice,

    /// <summary>IMMDevice::Activate would not hand over an IAudioSessionManager2.</summary>
    NoManager,

    /// <summary>The manager would not name this process's session on that endpoint.</summary>
    NoSession,

    /// <summary>The session is not an IAudioSessionControl2, which every supported Windows has.</summary>
    NoControl2,

    /// <summary>The system declined the preference; the HRESULT says why.</summary>
    Refused,
}

/// <summary>
/// PP840: asking Windows not to quiet this process's playback while its microphone is open.
///
/// THE PORT DUCKS THE GAME IT IS STREAMING. A capture on the communications endpoint turns on the
/// system's ducking experience, which attenuates every other render stream while it is open - 80
/// percent on the default setting. A listener heard the console's audio drop to almost nothing
/// while the run reported 762240 samples played, because the attenuation happens in the mixer and
/// XAudio2 cannot see it.
///
/// IT IS A RENDER CALL AND THE FIRST ATTEMPT PUT IT ON THE CAPTURE. SetDuckingPreference on the
/// microphone's own session answers AUDCLNT_E_WRONG_ENDPOINT_TYPE - 0x88890003, read off a live
/// run rather than guessed - because the experience it governs is a render one. So the session
/// this needs is the process's session on the RENDER endpoint, which is the one XAudio2 plays
/// into, and IAudioSessionManager2 hands that over without owning a stream on it.
///
/// THE DEVICE IS THE CONSOLE ROLE AND NOT THE COMMUNICATIONS ONE. What is being quieted is the
/// game's audio, which plays on the endpoint a person listens to - eCommunications there would ask
/// about a headset that may not be the one the stream is on.
///
/// REPORTED AND NEVER REQUIRED. A machine that refuses this still streams and still captures; what
/// it loses is the volume of its own playback, which is worse than refusing to open a microphone.
/// </summary>
public static class AudioDucking
{
    /// <summary>What the last <see cref="OptOut"/> came to.</summary>
    public static DuckingStep Stopped { get; private set; } = DuckingStep.NotAsked;

    /// <summary>The HRESULT behind it, or zero.</summary>
    public static int Error { get; private set; }

    /// <summary>
    /// Ask the system not to duck on this process's behalf.
    /// </summary>
    /// <returns>Whether the preference was accepted.</returns>
    public static bool OptOut()
    {
        Error = 0;

        if (Wasapi.Enumerator(out int made) is not { } enumerator)
        {
            Error = made;
            Stopped = DuckingStep.NoEnumerator;
            return false;
        }

        Wasapi.IMMDevice? device = null;

        try
        {
            Error = enumerator.GetDefaultAudioEndpoint(
                Wasapi.EDataFlow.Render, Wasapi.ERole.Console, out device);

            if (Error != 0 || device is null)
            {
                Stopped = DuckingStep.NoDevice;
                return false;
            }

            Error = device.Activate(
                Wasapi.AudioSessionManagerIid, Wasapi.ClsCtxAll, IntPtr.Zero, out object? activated);

            if (Error != 0 || activated is not Wasapi.IAudioSessionManager2 manager)
            {
                Stopped = DuckingStep.NoManager;
                return false;
            }

            try
            {
                return Preferred(manager);
            }
            finally
            {
                Marshal.ReleaseComObject(manager);
            }
        }
        finally
        {
            if (device is not null)
                Marshal.ReleaseComObject(device);

            Marshal.ReleaseComObject(enumerator);
        }
    }

    /// <summary>The session, and the one call this whole file exists for.</summary>
    private static bool Preferred(Wasapi.IAudioSessionManager2 manager)
    {
        // Null for the process's own default session, and no stream flags: this is not opening
        // anything, it is naming what XAudio2 already plays into.
        Error = manager.GetAudioSessionControl(IntPtr.Zero, 0, out object? session);

        if (Error != 0 || session is null)
        {
            Stopped = DuckingStep.NoSession;
            return false;
        }

        if (session is not Wasapi.IAudioSessionControl2 control)
        {
            Stopped = DuckingStep.NoControl2;
            Marshal.ReleaseComObject(session);
            return false;
        }

        try
        {
            Error = control.SetDuckingPreference(true);
            Stopped = Error == 0 ? DuckingStep.Ok : DuckingStep.Refused;

            return Error == 0;
        }
        finally
        {
            Marshal.ReleaseComObject(control);
        }
    }
}

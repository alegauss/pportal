using System.IO;
using ChiakiNg.Native;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP840: the ducking Windows does for a microphone, asked about and reported.
///
/// A capture on the communications endpoint turns on the system's ducking experience, which
/// attenuates every other render stream while it is open. A listener heard the console's audio drop
/// to almost nothing while the run reported 983040 samples played, because the attenuation is in
/// the mixer and nothing this port counts can see it.
///
/// WHAT THESE HOLD IS THE ASKING AND THE REPORTING, not the outcome. The system's answer is the
/// machine's - measured on this one, it accepts the preference and ducks anyway - so an assertion
/// that the sound is loud would be asserting about a Windows setting.
/// </summary>
public class AudioDuckingTests(ITestOutputHelper output)
{
    /// <summary>
    /// The opt-out is asked and answers, on a machine with speakers or without.
    ///
    /// Every step is named because they fail differently and a run has to say which: no enumerator
    /// is a COM refusal, no device is a machine with no output, and Refused is a system that heard
    /// the question. Only NotAsked would mean the call never ran.
    /// </summary>
    [Fact]
    public void TheOptOutIsAskedAndSaysWhereItGot()
    {
        bool accepted = AudioDucking.OptOut();

        output.WriteLine($"accepted={accepted} step={AudioDucking.Stopped} hr {AudioDucking.Error:X8}");

        Assert.NotEqual(DuckingStep.NotAsked, AudioDucking.Stopped);
        Assert.Equal(accepted, AudioDucking.Stopped == DuckingStep.Ok);

        // And Ok carries no error, which is what makes the HRESULT worth printing on the others.
        if (accepted)
            Assert.Equal(0, AudioDucking.Error);
    }

    /// <summary>
    /// PP840: it is a RENDER call, which is the correction a live run made.
    ///
    /// The first version asked the microphone's own session and got AUDCLNT_E_WRONG_ENDPOINT_TYPE -
    /// 0x88890003, read off a run rather than reasoned about. The experience the preference governs
    /// is a render one, so the session it needs is the process's on the output endpoint.
    ///
    /// Anchored on the flow and the role together: eCommunications there would ask about a headset
    /// that need not be the device the game is playing on.
    /// </summary>
    [Fact]
    public void TheSessionIsTheProcessesOwnOnTheOutputEndpoint()
    {
        string text = File.ReadAllText(
            SanitizerSource.LocateRelative(@"app\Native\AudioDucking.cs")
            ?? throw new InvalidOperationException("the ducking is not in this checkout."));

        Assert.Contains("Wasapi.EDataFlow.Render, Wasapi.ERole.Console", text, StringComparison.Ordinal);

        // Null session guid and no stream flags: the process's default session, not a new one.
        Assert.Contains("GetAudioSessionControl(IntPtr.Zero, 0", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The microphone asks for it, and the run says what it got.
    ///
    /// Read out of the source because both are reachable only from a session that wants a console.
    /// The report is half the point: "the sound went quiet" and "the microphone did not open" are
    /// different faults with the same complaint, and only one line separates them.
    /// </summary>
    [Fact]
    public void TheMicrophoneAsksAndTheRunPrintsWhatItGot()
    {
        string microphone = File.ReadAllText(
            SanitizerSource.LocateRelative(@"app\Session\ManagedMicrophone.cs")
            ?? throw new InvalidOperationException("the microphone is not in this checkout."));

        Assert.Matches(@"(?m)^\s*DuckingOptOut = AudioDucking\.OptOut\(\);", microphone);

        string run = File.ReadAllText(
            SanitizerSource.LocateRelative(@"app\Session\StreamRun.cs")
            ?? throw new InvalidOperationException("the run is not in this checkout."));

        Assert.Contains("capture.DuckingStopped", run, StringComparison.Ordinal);
        Assert.Contains("capture.DuckingError", run, StringComparison.Ordinal);
    }
}

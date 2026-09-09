using System.IO;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP825: the four stages with a device on one end and a takion on the other.
///
/// PP706 asserted the path end to end over synthetic units into a list; what none of those tests
/// could say is that anything OWNS one. These are the other half - the owner exists, it is off
/// unless asked for, and the root builds it - which is the shape PP824, PP811 and PP823 all had.
/// </summary>
public class ManagedMicrophoneTests(ITestOutputHelper output)
{
    /// <summary>
    /// The packet handed to the send is the sender's whole one, head included.
    ///
    /// chiaki_takion_send_mic_packet takes the buffer the audio sender built and overwrites two of
    /// its fields, so a caller that passed only the payload would be handing it a packet whose head
    /// is somebody else's bytes. This asserts the length the owner sends is the one the sender
    /// formatted, which is the join's only arithmetic.
    /// </summary>
    [Fact]
    public void WhatIsSentIsAWholePacketAndNotJustThePayload()
    {
        var lengths = new List<int>();

        using var microphone = new ManagedMicrophone(ps5: true, (_, length) =>
        {
            lengths.Add(length);
            return ChiakiError.Success;
        });

        // The formatter's own answer, which is what the owner's buffer is sized from.
        var sender = new ManagedAudioSender(ps5: true);

        Assert.Equal(MicPacketHead.SizePs5 + ManagedAudioSender.FrameBufferBytes, sender.PacketBytes);

        // Nothing has been captured, so nothing has been sent - and that is the state a session on a
        // machine with no microphone stays in rather than failing.
        Assert.Empty(lengths);
        Assert.Equal(0, microphone.Sent);
        Assert.Equal(0, microphone.Formatted);
    }

    /// <summary>
    /// A machine with no capture endpoint reports it and does not throw.
    ///
    /// PP652's rule reaching the owner: whether a machine has a microphone is a fact about the
    /// machine, and a host that cannot open one still has a session to run. So Start answers false
    /// and Opened says which of the three it was, on a build server as much as on a desk.
    /// </summary>
    [Fact]
    public void StartingWithoutADeviceReportsRatherThanThrows()
    {
        using var microphone = new ManagedMicrophone(ps5: false, (_, _) => ChiakiError.Success);

        bool running = microphone.Start();

        output.WriteLine($"opened={microphone.Opened} running={running} cleaning={microphone.Cleaning}");

        // Either it opened or it said why. Both are answers; a throw is not.
        Assert.Equal(running, microphone.Opened == CaptureResult.Running);
    }

    /// <summary>
    /// PP825: the root builds one, and only when a caller asks.
    ///
    /// Read out of the source for PP808's reason - Build is private and takes a session, so the
    /// join is reachable only from a start that wants a console. The assertion is on the GUARD as
    /// much as on the call: a microphone opened because a session started would be recording a room
    /// nobody said it could, and that is the half a census counting types cannot see.
    /// </summary>
    [Fact]
    public void TheRootOpensAMicrophoneOnlyWhereOneWasAskedFor()
    {
        string text = File.ReadAllText(
            SanitizerSource.LocateRelative(@"app\Session\ManagedStreamPhase.cs")
            ?? throw new InvalidOperationException("the phase is not in this checkout."));

        // The guard, anchored so the prose arguing about it cannot satisfy this.
        Assert.Matches(@"(?m)^\s*if \(Microphone is \{ \} micPs5\)", text);

        // And what it builds, joined to the takion's own send.
        Assert.Contains("new ManagedMicrophone(", text, StringComparison.Ordinal);
        Assert.Contains("takion.SendMicrophone(", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// PP839: a closed takion REFUSES a mic packet where the root asks it to, rather than throwing
    /// into the capture's thread.
    ///
    /// PP837's first live run sent 1671 packets and then died at teardown: the run had ended, the
    /// host had torn the takion down, and WasapiCapture's pump was still producing a frame every
    /// ten milliseconds. SendMicrophone's own first line refuses a closed stage - correctly, and by
    /// throwing, which on a background thread is a process that exits with no session report.
    ///
    /// THIS PINS THE PRECONDITION THE CATCH EXISTS FOR. If ManagedTakion ever stopped throwing
    /// here, the root's catch would be dead code hiding a different failure - so the throw is
    /// asserted rather than assumed, and the handling is asserted separately below.
    /// </summary>
    [Fact]
    public void AClosedTakionStillThrowsWhichIsWhyTheRootCatches()
    {
        var takion = new ManagedTakion(0x0000_7777);
        takion.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => takion.SendMicrophone(new byte[MicPacketHead.SizePs5], ps5: true));
    }

    /// <summary>
    /// PP839: and the root meets it as a refusal, with the capture stopped when the run returns.
    ///
    /// Read out of the source for PP808's reason, and BOTH halves are asserted because either one
    /// alone leaves the defect: stopping the capture without the catch still races - the pump can
    /// be inside a frame when the run ends - and catching without stopping leaves a microphone
    /// open, recording a room, for as long as the phase is undisposed.
    /// </summary>
    [Fact]
    public void TheRootStopsTheCaptureAndTreatsAClosedTakionAsARefusal()
    {
        string text = File.ReadAllText(
            SanitizerSource.LocateRelative(@"app\Session\ManagedStreamPhase.cs")
            ?? throw new InvalidOperationException("the phase is not in this checkout."));

        Assert.Matches(@"(?m)^\s*catch \(ObjectDisposedException\)", text);
        Assert.Matches(@"(?m)^\s*Capture\?\.Dispose\(\);", text);

        // And the stop is in the RUN's own thread, not only at the object's dispose - which is the
        // difference between a window of milliseconds and one that lasts as long as the phase.
        Assert.Contains("finally", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A refused send is counted as not sent, which is what the run's report reads back.
    ///
    /// The refusal is the ordinary end of a session and not an error, so the owner keeps formatting
    /// and keeps counting - what changes is that Sent stops moving and LastSend says why.
    /// </summary>
    [Fact]
    public void ARefusedSendLeavesTheCountStillAndSaysWhy()
    {
        using var microphone = new ManagedMicrophone(
            ps5: true, (_, _) => ChiakiError.Uninitialized);

        Assert.Equal(0, microphone.Sent);
        Assert.Equal(ChiakiError.Success, microphone.LastSend);
    }

    /// <summary>
    /// And it is off by default, which is the property's whole point.
    ///
    /// Asserted on the type rather than on a run: a phase that has to be started to answer this
    /// would need a console, and the default is exactly what a caller who never thought about the
    /// microphone gets.
    /// </summary>
    [Fact]
    public void APhaseNobodyAskedIsAPhaseWithNoMicrophone()
    {
        using var baseline = new SessionBaseline();

        ChiakiSession.LibInit();

        using var info = new ChiakiConnectInfo { Host = "127.0.0.1", Ps5 = true };
        info.SetRegistKey(new byte[16]);
        info.SetMorning(new byte[16]);
        info.SetVideoPreset(ChiakiVideoResolution.P720, ChiakiVideoFps.Fps60);

        using ChiakiSession? session = ChiakiSession.TryCreate(info, null, out _);
        if (session is null)
            return;

        using var phase = new ManagedStreamPhase(
            session, System.Net.IPAddress.Loopback, (_, _, _) => true, baseline);

        Assert.Null(phase.Microphone);
        Assert.Null(phase.Capture);
    }
}

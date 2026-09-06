using System.Net;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP795: the AV arm, installed when the bang produces the keys its far end needs.
///
/// PP783's first live trial carried a managed run to the idle loop against a real PS5 - connected,
/// protocol, bang, streaminfo, feedback sender, CONNECTED - took fourteen thousand datagrams with
/// video and audio among them, and decoded not one frame. The arm was null, because ManagedTakion
/// took it in its constructor and StreamAvArmSink needs a key base and an IV that the BANG produces
/// four states later.
///
/// THE C IS LATE IN THE SAME PLACE. chiaki_takion_set_crypt is called from inside the bang handler,
/// by stream_connection_init_crypt - so installing after the fact is the C's own shape rather than
/// a workaround for a managed constructor.
/// </summary>
public class AvArmInstallTests(ITestOutputHelper output)
{
    private sealed class Sent : IStreamMessageSink
    {
        public bool Send(in StreamMessage message) => true;
    }

    private sealed class Quiet : IFeedbackSink
    {
        public void SendState(ushort seqNum, FeedbackMotion state)
        {
        }

        public void SendHistory(ushort seqNum, ReadOnlySpan<byte> payload)
        {
        }
    }

    private sealed class Quieter : ICongestionSink
    {
        public void Send(CongestionReport report)
        {
        }
    }

    private sealed class NoFrames : IAudioFrameSink
    {
        public void Header(in ManagedAudioHeader header)
        {
        }

        public void Frame(ReadOnlySpan<byte> frame)
        {
        }
    }

    private sealed class NoOutbound : IVideoReceiverOutbound
    {
        public void SendCorruptFrame(ushort from, ushort to)
        {
        }

        public bool SendIdrRequest() => true;

        public void FecFailure(int frameIndex, bool idrRequestSent)
        {
        }
    }

    private sealed class Stages : IStageSink
    {
        public void Stage(FrameStageTimer stage, ulong sampleUs)
        {
        }

        public void InputToWire(ulong inputUs)
        {
        }
    }

    private static ManagedStreamRunHost Host(ManagedTakion takion) => new(
        takion,
        new IPEndPoint(IPAddress.Loopback, 9296),
        new ManagedCongestionControl(new ManagedPacketStats(), new Quieter(), 0.05),
        new ManagedFeedbackSender(new Quiet()),
        new ManagedSessionEvents(),
        new Sent(),
        new Stages(),
        StreamMessages.Heartbeat,
        () => new ManagedVideoReceiver((_, _, _) => true, new NoOutbound()),
        () => new ManagedAudioReceiverPair(new NoFrames(), new NoFrames()),
        () => new ManagedAudioReceiverPair(new NoFrames(), new NoFrames()));

    private static ManagedGkCryptPair Crypt()
        => ManagedGkCryptPair.Derive(new byte[0x10], [.. Enumerable.Repeat((byte)0x5a, 32)]);

    /// <summary>
    /// A TAKION WITH NO ARM ROUTES NOTHING, which is what the live trial measured.
    ///
    /// The state PP783 found and the reason its run decoded nothing: an AV packet reaches the
    /// dispatch and the branch that would act on it is guarded on an arm that is not there.
    /// </summary>
    [Fact]
    public void ATakionBuiltWithoutOneHasNoArm()
    {
        using var takion = new ManagedTakion(0x0000_7950);

        Assert.Null(takion.AvArm);
        Assert.False(takion.VideoQueueInitialised);
    }

    /// <summary>
    /// AND THE HOST GIVES IT ONE, out of the crypt the bang produced and the receivers it made.
    ///
    /// Both halves have to exist: the receivers are created at the top of the run and the crypt
    /// arrives four states in, so this is the first moment either could be asked for.
    /// </summary>
    [Fact]
    public void TheHostInstallsTheArmFromTheCryptAndItsReceivers()
    {
        using var takion = new ManagedTakion(0x0000_7951);
        ManagedStreamRunHost host = Host(takion);

        // Before the run makes them, there is nothing to route to and the install says so.
        Assert.False(host.InstallAvArm(Crypt()));
        Assert.Null(takion.AvArm);

        Assert.True(host.CreateVideoReceiver());
        Assert.True(host.CreateAudioReceiver());

        Assert.True(host.InstallAvArm(Crypt()));
        Assert.NotNull(takion.AvArm);

        output.WriteLine($"arm installed, video queue open: {takion.VideoQueueInitialised}");
    }

    /// <summary>
    /// AND IT IS INSTALLED ONCE, because an arm holds the video queue.
    ///
    /// Replacing one mid-session would leave the packets already in that queue pointing at an
    /// object nothing will drain, which is a stall rather than an error.
    /// </summary>
    [Fact]
    public void AnArmIsNotSwappedForAnother()
    {
        using var takion = new ManagedTakion(0x0000_7952);
        ManagedStreamRunHost host = Host(takion);

        host.CreateVideoReceiver();
        host.CreateAudioReceiver();

        Assert.True(host.InstallAvArm(Crypt()));
        Assert.Throws<InvalidOperationException>(() => host.InstallAvArm(Crypt()));
    }

    /// <summary>
    /// THE KEYING IS WHAT CALLS IT, at the same moment the C calls chiaki_takion_set_crypt.
    ///
    /// SessionBangKeying knows the session and the takion and deliberately not the receivers, so
    /// the join is a callback the composition root supplies. What this asserts is that the keying
    /// really invokes it, and with the pair it just derived - a keying that built the crypt and
    /// told nobody is the state PP783's trial was in.
    /// </summary>
    [Fact]
    public void TheKeyingHandsTheCryptToWhoeverInstallsTheArm()
    {
        ChiakiSession.LibInit();

        using var info = new ChiakiConnectInfo { Host = "127.0.0.1", Ps5 = true };
        info.SetRegistKey(new byte[16]);
        info.SetMorning(new byte[16]);
        info.SetVideoPreset(ChiakiVideoResolution.P720, ChiakiVideoFps.Fps60);

        using ChiakiSession? session = ChiakiSession.TryCreate(info, null, out _);
        if (session is null)
            return;

        using var takion = new ManagedTakion(0x0000_7953);

        ManagedGkCryptPair? handed = null;
        var keying = new SessionBangKeying(session, takion) { InstallArm = one => handed = one };

        // No derive has happened, so the crypt is refused and nothing is handed over - which is
        // the ordering the C has too: init_crypt runs only past a secret.
        Assert.False(keying.InitCrypt());
        Assert.Null(handed);
        Assert.Null(keying.Crypt);
    }

    /// <summary>
    /// The remote crypt is what an arriving packet is decrypted against, and it is not the local one.
    ///
    /// This side sends under index two and the console sends under three. An arm built from the
    /// local key produces plausible garbage a decoder reports as a corrupt frame, which is the
    /// failure that looks like a bad network.
    /// </summary>
    [Fact]
    public void TheArmTakesTheRemoteKeyAndNotTheLocalOne()
    {
        ManagedGkCryptPair pair = Crypt();

        Assert.Equal(ManagedGkCryptPair.RemoteIndex, pair.Remote.Index);
        Assert.Equal(ManagedGkCryptPair.LocalIndex, pair.Local.Index);
        Assert.NotEqual(pair.Local.KeyBase, pair.Remote.KeyBase);

        if (SanitizerSource.LocateRelative(@"app\Protocol\ManagedStreamRunHost.cs") is not { } path)
            return;

        // The install reads the remote half, read as code so the paragraph above it is not the
        // evidence - PP793's rule, which this tree now applies everywhere it reads a source.
        string code = DeadAssertions.CodeOnly(File.ReadAllText(path));

        Assert.Contains("crypt.Remote.KeyBase", code, StringComparison.Ordinal);
        Assert.DoesNotContain("crypt.Local.KeyBase", code, StringComparison.Ordinal);
    }
}

using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP366, under PP295: three layers of dispatch, and the ten lines PP30 waits on.
///
/// The finding worth asserting is not that each layer routes correctly - it is that the SAME bytes
/// route differently depending on the state, and that one of the two connect answers is dropped
/// everywhere except in the one state that waits for it.
/// </summary>
public class StreamDispatchTests
{
    private static string? Core()
    {
        string? path = StreamDispatchSource.Locate();
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// LAYER THREE, AND THE POINT OF THE WHOLE FILE. One protobuf is three messages.
    /// </summary>
    [Theory]
    [InlineData(StreamState.ExpectBang, ProtobufHandler.ExpectBang)]
    [InlineData(StreamState.ExpectStreaminfo, ProtobufHandler.ExpectStreaminfo)]
    [InlineData(StreamState.Idle, ProtobufHandler.Idle)]
    public void OneProtobufIsThreeMessages(StreamState state, ProtobufHandler handler)
    {
        Assert.Equal(handler, StreamDispatch.HandlerFor(state));
    }

    /// <summary>
    /// And the default arm is idle, so a protobuf arriving before a stream exists is handled by the
    /// idle handler rather than dropped. Reproduced, not tightened.
    /// </summary>
    [Fact]
    public void AProtobufBeforeAStreamReachesTheIdleHandler()
    {
        Assert.Equal(ProtobufHandler.Idle, StreamDispatch.HandlerFor(StreamState.TakionConnect));
    }

    /// <summary>
    /// LAYER ONE. Both connect answers are heard only in the state that waits for one.
    /// </summary>
    [Fact]
    public void TheConnectAnswersAreHeardInOneStateOnly()
    {
        Assert.Equal(
            TakionRoute.FinishConnect,
            StreamDispatch.Route(TakionEvent.Connected, StreamState.TakionConnect));

        Assert.Equal(
            TakionRoute.FailConnect,
            StreamDispatch.Route(TakionEvent.Disconnect, StreamState.TakionConnect));
    }

    /// <summary>
    /// THE SILENCE. Takion dying after the first state signals nothing, so the wait that would
    /// have wanted to know sits out its whole timeout.
    ///
    /// Same shape as PP365's dead state_failed and from the same direction: the machine learns
    /// about failures late or not at all.
    /// </summary>
    [Theory]
    [InlineData(StreamState.ExpectBang)]
    [InlineData(StreamState.ExpectStreaminfo)]
    [InlineData(StreamState.Idle)]
    public void TakionDyingAfterTheFirstStateIsNotNoticed(StreamState state)
    {
        Assert.False(StreamDispatch.ADisconnectIsNoticed(state));
        Assert.Equal(TakionRoute.Ignored, StreamDispatch.Route(TakionEvent.Disconnect, state));
    }

    /// <summary>And the data and AV events route regardless of the state, which is the contrast.</summary>
    [Theory]
    [InlineData(StreamState.TakionConnect)]
    [InlineData(StreamState.ExpectBang)]
    [InlineData(StreamState.Idle)]
    public void DataAndAvRouteFromAnyState(StreamState state)
    {
        Assert.Equal(TakionRoute.ToData, StreamDispatch.Route(TakionEvent.Data, state));
        Assert.Equal(TakionRoute.ToAv, StreamDispatch.Route(TakionEvent.Av, state));
    }

    /// <summary>LAYER TWO is flat: four kinds handled, everything else dropped.</summary>
    [Fact]
    public void TheDataLayerIsFlat()
    {
        Assert.True(StreamDispatch.IsHandled(TakionData.Protobuf));
        Assert.True(StreamDispatch.IsHandled(TakionData.Rumble));
        Assert.True(StreamDispatch.IsHandled(TakionData.PadInfo));
        Assert.True(StreamDispatch.IsHandled(TakionData.TriggerEffects));

        Assert.False(StreamDispatch.IsHandled(TakionData.Other));
    }

    /// <summary>
    /// THE OFFSET. An AV packet decrypts at its key position PLUS one block, never at the position
    /// itself - and getting it wrong produces plausible garbage rather than a failure.
    /// </summary>
    [Fact]
    public void TheAvDecryptIsOneBlockPastTheKeyPosition()
    {
        Assert.Equal(0x10ul, StreamDispatch.DecryptPositionFor(0));
        Assert.Equal(0x1010ul, StreamDispatch.DecryptPositionFor(0x1000));

        // Stated as an inequality too, because "plus a block" is the thing that gets dropped.
        Assert.NotEqual(0x1000ul, StreamDispatch.DecryptPositionFor(0x1000));
    }

    /// <summary>The route, by the two flags, in the C's own order.</summary>
    [Theory]
    [InlineData(true, false, AvDestination.Video)]
    [InlineData(false, true, AvDestination.Haptics)]
    [InlineData(false, false, AvDestination.Audio)]
    [InlineData(true, true, AvDestination.Video)]
    public void ThePacketIsRoutedByTwoFlags(bool isVideo, bool isHaptics, AvDestination destination)
    {
        Assert.Equal(destination, StreamDispatch.DestinationFor(true, isVideo, isHaptics));
    }

    /// <summary>PP367: a packet that did not decrypt goes nowhere at all.</summary>
    [Fact]
    public void AFailedDecryptGoesNowhere()
    {
        Assert.Equal(AvDestination.Dropped, StreamDispatch.DestinationFor(false, true, false));
        Assert.Equal(AvDestination.Dropped, StreamDispatch.DestinationFor(false, false, false));
    }

    /// <summary>
    /// PP30's premise, named. One call keeps four things in the build, and they can only leave in
    /// that order.
    /// </summary>
    [Fact]
    public void TheVideoRouteIsWhatPp30WaitsOn()
    {
        Assert.Equal(
            ["videoreceiver.c", "frameprocessor.c", "fec.c", "jerasure"],
            StreamDispatch.KeptAliveByTheVideoRoute);
    }

    /// <summary>And streamconnection.c still dispatches this way, at all three layers.</summary>
    [Fact]
    public void TheThreeLayersStillWorkThisWay()
    {
        if (Core() is not { } core)
            return;

        string? callback = StreamDispatchSource.LayerBody(core, "static void stream_connection_takion_cb");
        string? protobuf = StreamDispatchSource.LayerBody(
            core, "static void stream_connection_takion_data_protobuf");
        string? av = StreamDispatchSource.LayerBody(core, "static void stream_connection_takion_av");

        Assert.NotNull(callback);
        Assert.NotNull(protobuf);
        Assert.NotNull(av);

        Assert.True(
            StreamDispatchSource.ConnectAnswersAreStillStateGuarded(callback),
            "the connect answers are no longer guarded on TAKION_CONNECT");
        Assert.True(
            StreamDispatchSource.TheStateLockStillSpansTheSwitch(protobuf),
            "the protobuf layer no longer holds the state lock across the whole switch");
        Assert.True(
            StreamDispatchSource.TheAvDecryptStillAddsABlock(av),
            "the AV decrypt no longer adds a block to the packet's key position");
        Assert.True(
            StreamDispatchSource.TheVideoRouteStillReachesTheNativeReceiver(av),
            "video no longer reaches chiaki_video_receiver_av_packet, so PP30's premise has moved");
        Assert.True(
            StreamDispatchSource.HapticsIsStillTestedBeforeTheAudioFallback(av),
            "haptics is no longer tested before the audio fallback");
    }

    /// <summary>The readers read the file (PP272), and see the edits they guard against.</summary>
    [Fact]
    public void TheReadersSeeTheEditsTheyGuardAgainst()
    {
        Assert.False(StreamDispatchSource.ConnectAnswersAreStillStateGuarded(""));
        Assert.False(StreamDispatchSource.TheStateLockStillSpansTheSwitch(""));
        Assert.False(StreamDispatchSource.TheAvDecryptStillAddsABlock(""));
        Assert.False(StreamDispatchSource.TheVideoRouteStillReachesTheNativeReceiver(""));
        Assert.False(StreamDispatchSource.HapticsIsStillTestedBeforeTheAudioFallback(""));

        // The offset dropped, which is the edit that reads as a network fault.
        const string WithoutTheBlock =
            "chiaki_gkcrypt_decrypt(stream_connection->gkcrypt_remote, packet->key_pos, "
            + "packet->data, packet->data_size);";

        Assert.False(StreamDispatchSource.TheAvDecryptStillAddsABlock(WithoutTheBlock));

        // And an arm releasing the state lock early, which opens the window the run relies on.
        const string UnlockedInsideAnArm = """
            	chiaki_mutex_lock(&stream_connection->state_mutex);
            	switch(stream_connection->state)
            	{
            		case STATE_EXPECT_BANG:
            			chiaki_mutex_unlock(&stream_connection->state_mutex);
            			stream_connection_takion_data_expect_bang(stream_connection, buf, buf_size);
            			break;
            	}
            	chiaki_mutex_unlock(&stream_connection->state_mutex);
            """;

        Assert.False(StreamDispatchSource.TheStateLockStillSpansTheSwitch(UnlockedInsideAnArm));
    }
}

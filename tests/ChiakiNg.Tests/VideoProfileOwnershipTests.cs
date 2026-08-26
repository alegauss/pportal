using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP372: who owns a decoded video header, and which exits owe a free.
///
/// PP297's capture has one resolution and a good audio header, so it takes the single path where the
/// handover happens and nothing leaks. Every path this covers is one that capture does not reach.
/// </summary>
public class VideoProfileOwnershipTests
{
    /// <summary>
    /// THE HANDOVER IS THE ONLY THING THAT MOVES OWNERSHIP, and only when it succeeds.
    ///
    /// Declining is not a transfer. The receiver copies the array in one memcpy or not at all, so a
    /// refusal leaves every header exactly where it was - which is what the void return hid.
    /// </summary>
    [Theory]
    [InlineData(false, null, ProfileOwner.Nobody)]
    [InlineData(true, null, ProfileOwner.TheContext)]
    [InlineData(true, false, ProfileOwner.TheContext)]
    [InlineData(true, true, ProfileOwner.TheReceiver)]
    public void OwnershipMovesOnlyOnAnAcceptedHandover(
        bool decoded, bool? accepted, ProfileOwner expected)
    {
        Assert.Equal(expected, VideoProfileOwnership.OwnerAfter(decoded, accepted));
    }

    /// <summary>
    /// And an exit owes a free exactly where the context still owns them.
    ///
    /// Both directions matter. Missing the free is the leak; adding one after an accepted handover is
    /// a double free, which is worse.
    /// </summary>
    [Fact]
    public void AnExitOwesAFreeExactlyWhereTheContextStillOwnsThem()
    {
        Assert.False(VideoProfileOwnership.MustFree(ProfileOwner.Nobody));
        Assert.True(VideoProfileOwnership.MustFree(ProfileOwner.TheContext));
        Assert.False(VideoProfileOwnership.MustFree(ProfileOwner.TheReceiver));
    }

    /// <summary>
    /// THE COUNT IS THE CONSOLE'S AND THE ROOM IS NOT, so nothing is padded that cannot be kept.
    ///
    /// That equality is the fix. Below the realloc, the check let one header be allocated and padded
    /// per announced resolution and kept only the first eight - so a console announcing fifty left
    /// forty-two padded buffers with nothing owning them.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(50)]
    public void NothingIsPaddedThatCannotBeKept(int announced)
    {
        Assert.Equal(
            VideoProfileOwnership.HeadersKept(announced),
            VideoProfileOwnership.HeadersPadded(announced));

        Assert.True(VideoProfileOwnership.HeadersKept(announced) <= VideoProfileOwnership.ProfilesMax);
    }

    /// <summary>And the C still checks the count before it pads.</summary>
    [Fact]
    public void TheCountIsCheckedBeforeTheHeaderIsPadded()
    {
        string? path = VideoProfileOwnershipSource.Locate(
            VideoProfileOwnershipSource.StreamRelativePath);
        if (path is null)
            return;

        Assert.True(
            VideoProfileOwnershipSource.TheCountIsCheckedBeforeTheHeaderIsPadded(File.ReadAllText(path)),
            "the profile count is checked after the header is padded, so a header past the maximum is allocated and dropped");
    }

    /// <summary>
    /// And no exit from the streaminfo handler leaves while the context still owns the headers.
    ///
    /// This is the check that would have caught the original: the audio-header branch reached the
    /// error label with every resolution already decoded and padded, and freed none of them.
    /// </summary>
    [Fact]
    public void NoExitLosesTheHeaders()
    {
        string? path = VideoProfileOwnershipSource.Locate(
            VideoProfileOwnershipSource.StreamRelativePath);
        if (path is null)
            return;

        IReadOnlyList<string> losing =
            VideoProfileOwnershipSource.ExitsThatLoseTheHeaders(File.ReadAllText(path));

        Assert.True(
            losing.Count == 0,
            "these exits leave the decoded video headers with nothing owning them:\n  "
                + string.Join("\n  ", losing));
    }

    /// <summary>And the handover answers whether it took them, in both the definition and the promise.</summary>
    [Fact]
    public void TheHandoverAnswersWhetherItTookThem()
    {
        string? receiver = VideoProfileOwnershipSource.Locate(
            VideoProfileOwnershipSource.ReceiverRelativePath);
        string? header = VideoProfileOwnershipSource.Locate(
            VideoProfileOwnershipSource.ReceiverHeaderRelativePath);
        if (receiver is null || header is null)
            return;

        Assert.True(
            VideoProfileOwnershipSource.TheHandoverAnswers(
                File.ReadAllText(receiver), File.ReadAllText(header)),
            "the handover is void again, so a caller cannot tell a transfer from a refusal");
    }

    /// <summary>
    /// And the readers find the file as it was, so the three checks mean something.
    /// </summary>
    [Fact]
    public void TheReadersFindTheOldOrderAndTheOldExits()
    {
        const string calbackAsItWas = """
            static bool pb_decode_resolution(pb_istream_t *stream, const pb_field_t *field, void **arg)
            {
            	uint8_t *header_buf_padded = realloc(header_buf.buf, header_buf.size + CHIAKI_VIDEO_BUFFER_PADDING_SIZE);
            	if(ctx->video_profiles_count >= CHIAKI_VIDEO_PROFILES_MAX)
            	{
            		CHIAKI_LOGE(ctx->stream_connection->session->log, "Received more resolutions than the maximum");
            		return true;
            	}
            	return true;
            }
            """;

        Assert.False(
            VideoProfileOwnershipSource.TheCountIsCheckedBeforeTheHeaderIsPadded(calbackAsItWas));

        const string handlerAsItWas = """
            static void stream_connection_takion_data_expect_streaminfo(ChiakiStreamConnection *stream_connection, uint8_t *buf, size_t buf_size)
            {
            	if(!r)
            	{
            		CHIAKI_LOGE(stream_connection->log, "StreamConnection failed to decode data protobuf");
            		return;
            	}
            	if(audio_header_buf.size != CHIAKI_AUDIO_HEADER_SIZE)
            	{
            		CHIAKI_LOGE(stream_connection->log, "StreamConnection received invalid audio header in streaminfo");
            		goto error;
            	}
            	chiaki_video_receiver_stream_info(stream_connection->video_receiver, p, n);
            }
            """;

        Assert.Equal(
            ["return;", "goto error;"],
            VideoProfileOwnershipSource.ExitsThatLoseTheHeaders(handlerAsItWas));

        Assert.False(VideoProfileOwnershipSource.TheHandoverAnswers(
            "CHIAKI_EXPORT void chiaki_video_receiver_stream_info(ChiakiVideoReceiver *r)",
            "CHIAKI_EXPORT void chiaki_video_receiver_stream_info(ChiakiVideoReceiver *r);"));
    }

    /// <summary>And a handler it cannot find is a failure, not a pass.</summary>
    [Fact]
    public void AMissingHandlerIsAFailure()
    {
        Assert.NotEmpty(VideoProfileOwnershipSource.ExitsThatLoseTheHeaders(string.Empty));
    }
}

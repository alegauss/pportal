using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP528, and the number PP76 will read.
///
/// The session record carries two loss counters and only one of them could ever differ by
/// decoder: <c>frames_lost</c> is the video receiver's own total, counted upstream of every
/// decoder and identical whichever one runs. <c>frames_dropped</c> is the decoder-side view, and
/// it was under-reported because the count is consumed by the pull that hands it over - two
/// returns between there and the presenter dropped it, and both of them are decoder dependent.
///
/// So this holds the region rather than the repair. What matters is not that today's two returns
/// carry the count; it is that a third one added later cannot quietly not.
/// </summary>
public class FrameLossAccountingTests
{
    private static string? Source()
    {
        string? path = FrameLossAccounting.Locate();
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// The region exists at all. Asserted separately because every claim below is vacuous over an
    /// empty region: a handler that stopped pulling, or stopped presenting, would make the return
    /// sweep find nothing and report that as compliance.
    /// </summary>
    [Fact]
    public void TheHandlerStillPullsAndStillPresents()
    {
        if (Source() is not { } source)
            return;

        Assert.NotEmpty(FrameLossAccounting.RegionLines(source));
    }

    /// <summary>
    /// Every early return between the pull and the present carries the count forward.
    ///
    /// This is the defect itself, stated as a rule instead of as two line numbers. A return that
    /// leaves without touching the accumulator takes with it a loss that the record will never
    /// hear about, and on the hardware-transfer path it takes the ones the slower decoder produced.
    /// </summary>
    [Fact]
    public void NoReturnBetweenThePullAndThePresentDropsTheCount()
    {
        if (Source() is not { } source)
            return;

        var returns = FrameLossAccounting.EarlyReturns(source);
        Assert.NotEmpty(returns);
        Assert.All(returns, r => Assert.True(r.CarriesTheCount,
            $"the return at region line {r.Line} leaves without carrying {FrameLossAccounting.Carrier}"));
    }

    /// <summary>
    /// And the sweep can tell the difference. A rule that passed over a region with no returns in
    /// it, or that could not see a return that drops the count, would be green on the code this
    /// task was filed against - so it is run here against that code, written out.
    /// </summary>
    [Fact]
    public void ASweepThatCannotSeeADroppedCountIsNotASweep()
    {
        const string before = """
                ChiakiFfmpegFrame frame = chiaki_ffmpeg_decoder_pull_frame(decoder, &frames_lost);
                if (!frame.frame)
                    return;
                if (!prepareFrameForPresentation(frame, use_opengl_renderer))
                {
                    av_frame_free(&frame.frame);
                    return;
                }
                target_window->presentFrame(frame, frames_lost, delivery_us);
            """;

        var dropped = FrameLossAccounting.EarlyReturns(before);
        Assert.Equal(2, dropped.Count);
        Assert.All(dropped, r => Assert.False(r.CarriesTheCount));

        const string after = """
                ChiakiFfmpegFrame frame = chiaki_ffmpeg_decoder_pull_frame(decoder, &frames_lost);
                frames_lost += carried_frames_lost.fetchAndStoreRelaxed(0);
                if (!frame.frame) {
                    carried_frames_lost.fetchAndAddRelaxed(frames_lost);
                    return;
                }
                if (!prepareFrameForPresentation(frame, use_opengl_renderer))
                {
                    carried_frames_lost.fetchAndAddRelaxed(frames_lost);
                    av_frame_free(&frame.frame);
                    return;
                }
                target_window->presentFrame(frame, frames_lost, delivery_us);
            """;

        var carried = FrameLossAccounting.EarlyReturns(after);
        Assert.Equal(2, carried.Count);
        Assert.All(carried, r => Assert.True(r.CarriesTheCount));
    }
}

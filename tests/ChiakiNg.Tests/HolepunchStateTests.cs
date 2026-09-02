using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP478, PP340: what the PSN flow holds between the nine calls, and for how long.
///
/// §PP340 names three things missing - calling the pieces in order, deciding what a failure does, and
/// holding the state between them. PP460 did the first two; this is the third.
///
/// The assertion worth the task is the registration info: `info.holepunch_info` points at a stack local
/// and is handed to a call that starts a thread, so it is sound only because four calls all finish
/// inside that local's block. Nothing says so.
/// </summary>
public class HolepunchStateTests
{
    /// <summary>
    /// PP631: session.c while it still asks - null outside a checkout, and null once PP33's flip
    /// lands, so these decline rather than fail and PP630's counterpart asserts the deletion.
    /// </summary>
    private static string? Session() => SessionHolepunchShape.AskingSource();

    /// <summary>
    /// FIVE PIECES, THREE LIFETIMES - and a managed flow object would give them all one.
    /// </summary>
    [Fact]
    public void FivePiecesAcrossThreeLifetimes()
    {
        Assert.Equal(5, HolepunchState.Carried.Count);

        Assert.Equal(3, HolepunchState.Carried.Select(s => s.Lifetime).Distinct().Count());

        // Every piece comes from one of the nine steps, so nothing is carried that nothing produces.
        foreach (FlowState state in HolepunchState.Carried)
        {
            Assert.NotNull(state.FromStep);
            Assert.Contains(state.FromStep!.Value, HolepunchFlow.ExecutionOrder);
        }
    }

    /// <summary>
    /// One piece dies before the connect does, and it is the registration info.
    ///
    /// Single() rather than a count: if a second block-scoped piece ever appears, this throws rather
    /// than quietly agreeing.
    /// </summary>
    [Fact]
    public void OnlyTheRegistInfoIsBlockScoped()
    {
        FlowState shortest = HolepunchState.TheShortestLived;

        Assert.Equal("hinfo", shortest.Name);
        Assert.Equal(StateLifetime.Block, shortest.Lifetime);
        Assert.Equal(HolepunchStep.RegistInfo, shortest.FromStep);
    }

    /// <summary>
    /// THE POINTER TO A STACK LOCAL, safe by scope alone: all four regist calls finish inside the block
    /// that owns the info they read through.
    ///
    /// Move any of the four out and the regist thread reads a dead frame. Nothing in the C says so,
    /// which is the reason to assert it rather than describe it.
    /// </summary>
    [Fact]
    public void AllFourRegistCallsStayInsideTheInfosBlock()
    {
        Assert.Equal(4, HolepunchState.MustStayWithTheRegistInfo.Count);

        if (Session() is not { } source)
            return;

        Assert.True(
            HolepunchState.TheRegistCallsStayWithTheInfo(source),
            "a regist call moved out of the block that owns hinfo, so the thread it starts may read a "
                + "dead frame");
    }

    /// <summary>
    /// The data socket's null is a VALUE - a local session - not an unset field.
    ///
    /// PP461 traced that; this is where a managed flow would get it wrong, by treating null as "not yet
    /// assigned" and refusing to stream locally.
    /// </summary>
    [Fact]
    public void ANullDataSocketMeansALocalSession()
    {
        Assert.Contains("local session", HolepunchState.NullDataSocketMeans);

        if (Session() is not { } source)
            return;

        Assert.True(
            HolepunchState.TheDataSocketIsDeclaredOutsideThePsnBlock(source),
            "the data socket moved inside the PSN block, so its null no longer reaches the local path");
    }

    /// <summary>
    /// And the selected address is written straight into the session's hostname, not returned - so a
    /// managed flow keeping an address of its own would have two to hold in step.
    /// </summary>
    [Fact]
    public void TheAddressIsWrittenIntoTheSessionsOwnBuffer()
    {
        if (Session() is not { } source)
            return;

        Assert.True(HolepunchState.TheAddressIsStillWrittenInPlace(source));
    }

    /// <summary>PP272: and the readers say no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.False(HolepunchState.TheRegistCallsStayWithTheInfo(""));
        Assert.False(HolepunchState.TheDataSocketIsDeclaredOutsideThePsnBlock(""));
        Assert.False(HolepunchState.TheAddressIsStillWrittenInPlace(""));
    }
}

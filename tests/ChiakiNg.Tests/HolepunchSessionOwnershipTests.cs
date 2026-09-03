using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP592, under PP33: the two finis among session.c's nine asks are ownership, not duplication.
///
/// Read as a list of call sites, chiaki_holepunch_session_fini looks like the cheapest two of the
/// nine to remove: hand the destruction back to whoever created the session. This is what says the
/// reading is wrong, and it is worth an assertion rather than a comment because the tracing is the
/// expensive part - three files and a header, to learn that a call is where it belongs.
/// </summary>
public class HolepunchSessionOwnershipTests
{
    /// <summary>
    /// The Qt client creates the session and never destroys it, so session.c's two sites are the
    /// only destructor it has.
    ///
    /// streamsession.cpp calls chiaki_holepunch_session_init, drives UPnP discover, create, offer,
    /// start and the ctrl punch, then hands the handle over as chiaki_connect_info.holepunch_session
    /// and stops. Moving session.c's finis to the caller leaks the PSN session, the websocket and
    /// UPnP threads, the port mappings and the curl share.
    /// </summary>
    [Fact]
    public void TheQtClientCreatesTheSessionAndLeavesTheDestruction()
    {
        if (HolepunchSessionOwnership.LocateQtClient() is not { } path)
            return;

        Assert.True(
            HolepunchSessionOwnership.TheQtClientCreatesAndDoesNotDestroy(File.ReadAllText(path)),
            "streamsession.cpp either stopped creating the holepunch session or started destroying "
                + "it - either way session.c's two fini sites are a different call now");
    }

    /// <summary>
    /// THE GUARD: the shim never writes the handle into the connect info it hands
    /// chiaki_session_init.
    ///
    /// The shim wraps fini for the managed driver, so there are two owners of a holepunch session in
    /// this tree. They cannot meet today, and only because of this: chiaki_shim_session_create passes
    /// a connect info whose holepunch_session the shim never sets, so the C session it builds carries
    /// a null handle and chiaki_session_fini's fini never runs on a managed one.
    ///
    /// Wiring it in is the natural next move for anything porting the PSN path - the Qt client does
    /// exactly that at chiaki_connect_info.holepunch_session - and the day it happens, both owners
    /// fini one handle. A double free is not a test failure anywhere else in this suite.
    ///
    /// PP655: ROUTED THROUGH THE SEAM'S SHAPE, which is that line's first step. There are two owners
    /// only while the shim wraps fini at all; once PP655's flip takes the nine wrappers behind an
    /// option there is one owner and this has nothing to be about. Asking
    /// <see cref="ShimHolepunchShape"/> is what lets the flip edit no test file - and the
    /// counterpart below is what stops the question becoming a way of not looking.
    /// </summary>
    [Fact]
    public void TheShimKeepsItsHandleOutOfTheConnectInfo()
    {
        if (ShimHolepunchShape.WrappingHeader() is null)
            return;

        if (HolepunchSessionOwnership.LocateShim() is not { } path)
            return;

        Assert.True(
            HolepunchSessionOwnership.TheShimNeverWiresTheHandleIn(File.ReadAllText(path)),
            "the shim now puts a holepunch handle into the connect info, so chiaki_session_fini and "
                + "chiaki_shim_holepunch_session_fini are two owners of one session - decide which");
    }

    /// <summary>
    /// PP655: and once the seam is bare there is one owner, which is the other half of the pair.
    ///
    /// The assertion that runs after the flip. It declines today, exactly as the one above will
    /// decline after - and between them one of the two always runs, which is what makes this a
    /// conversion rather than a guard somebody bolted on.
    /// </summary>
    [Fact]
    public void OnceTheSeamIsBareThereIsOneOwner()
    {
        if (ShimHolepunchShape.BareHeader() is not { } header)
            return;

        Assert.Empty(ShimHolepunchShape.StillDeclaredIn(header));
    }

    /// <summary>
    /// And the guard can see the assignment it is watching for, in the shape the Qt client writes
    /// it. A check on an absence is green on an empty file, which is the failure mode this one has.
    /// </summary>
    [Fact]
    public void TheGuardSeesTheAssignmentItWatchesFor()
    {
        Assert.False(HolepunchSessionOwnership.TheShimNeverWiresTheHandleIn(
            "\tinfo->info.holepunch_session = handle;"));

        Assert.False(HolepunchSessionOwnership.TheShimNeverWiresTheHandleIn(
            "\tchiaki_connect_info.holepunch_session = holepunch_session;"));
    }

    /// <summary>
    /// And it reads neither a comment about the field nor a test of it.
    ///
    /// The shim's own comments discuss the holepunch at length, and its wrappers are named for the
    /// field - so a rule that banned the word would fail on chiaki_shim_holepunch_session_init, and
    /// one that read comments would fail on the paragraph explaining why the field stays unset.
    /// </summary>
    [Fact]
    public void CommentsAndComparisonsAreNotAssignments()
    {
        Assert.True(HolepunchSessionOwnership.TheShimNeverWiresTheHandleIn(
            "\t// info->info.holepunch_session = handle; would give this two owners"));

        Assert.True(HolepunchSessionOwnership.TheShimNeverWiresTheHandleIn(
            " * The connect info's holepunch_session = whatever the caller had."));

        Assert.True(HolepunchSessionOwnership.TheShimNeverWiresTheHandleIn(
            "\tif(info->info.holepunch_session == NULL)\n\t\treturn;"));

        Assert.True(HolepunchSessionOwnership.TheShimNeverWiresTheHandleIn(
            "CHIAKI_SHIM_API void *chiaki_shim_holepunch_session_init(const char *token)"));
    }

    /// <summary>
    /// And the two the seam records are still two, so this is about the sites it names.
    ///
    /// PP429's list is held by name and relative position; if the finis stopped being two of the
    /// nine, this file would be arguing about calls that are not there.
    /// </summary>
    [Fact]
    public void TheSeamStillRecordsTwoFinis()
    {
        Assert.Equal(
            2,
            HolepunchSeam.Asks.Count(ask => ask.Callee == HolepunchSessionOwnership.Fini));
    }
}

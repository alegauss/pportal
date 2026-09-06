using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP788, under PP784: senkusha's state walk, held against the file it is a model of.
///
/// Five models of senkusha already existed and none was this one - where the machine is, and what
/// ends the wait it is making. PP362 wrote that half for the stream connection and its finding was
/// that a whole flag is dead; senkusha has the same pair and the same silence, plus one of its own.
///
/// A STATE THE MACHINE CANNOT BE IN. STATE_EXPECT_STREAMINFO_ACK is declared and never assigned,
/// so nine states describe eight. Worth a row rather than a deletion: the enum's values are what a
/// reader matches against, and dropping the member would renumber every state after it.
/// </summary>
public class SenkushaStatesTests(ITestOutputHelper output)
{
    private static string? Source()
        => SenkushaStatesSource.Locate() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// EVERY STATE THIS MODEL NAMES IS ONE THE FILE DECLARES, and there are nine.
    ///
    /// Read from senkusha.c rather than trusted, so a state added upstream fails here by name
    /// instead of being absent from a port nobody compared.
    /// </summary>
    [Fact]
    public void TheNineStatesAreTheFilesOwn()
    {
        Assert.Equal(9, SenkushaStates.Declared.Count);

        if (Source() is not { } source)
            return;

        Assert.All(
            SenkushaStates.Declared,
            one => Assert.True(
                SenkushaStatesSource.Declares(source, one),
                $"senkusha.c no longer declares {SenkushaStatesSource.NameOf(one)}"));
    }

    /// <summary>
    /// AND ONE OF THE NINE IS NEVER ENTERED, which is the finding.
    ///
    /// Both directions: the eight reachable ones are assigned somewhere in the file, and the ninth
    /// is not. A state that started being entered would make this red, which is the right answer -
    /// the walk would then have a step nothing here models.
    /// </summary>
    [Fact]
    public void OneStateIsDeclaredAndNeverEntered()
    {
        if (Source() is not { } source)
            return;

        Assert.False(
            SenkushaStatesSource.IsEntered(source, SenkushaStates.Unreachable),
            "the streaminfo-ack state is entered now, so the walk has a step this does not model");

        output.WriteLine($"{SenkushaStates.Reachable.Count} reachable of {SenkushaStates.Declared.Count}");

        foreach (SenkushaState one in SenkushaStates.Reachable)
        {
            // Idle is entered by init rather than by the run, and the assignment reads the same.
            Assert.True(
                SenkushaStatesSource.IsEntered(source, one),
                $"{SenkushaStatesSource.NameOf(one)} is never assigned, so it is unreachable too");
        }
    }

    /// <summary>
    /// THE PREDICATE READS TWO FIELDS AND THERE ARE THREE, which is PP365's finding in this file.
    ///
    /// A wait ends on the state finishing or on somebody stopping the session, and never on the
    /// state failing. Reproduced rather than repaired: ending a wait on failure would report
    /// failures sooner than the C, which is better behaviour and different behaviour.
    /// </summary>
    [Fact]
    public void AWaitEndsOnFinishedOrStoppedAndNeverOnFailed()
    {
        Assert.True(SenkushaStates.WaitEnds(new SenkushaWaitState(Finished: true)));
        Assert.True(SenkushaStates.WaitEnds(new SenkushaWaitState(ShouldStop: true)));
        Assert.False(SenkushaStates.WaitEnds(new SenkushaWaitState(Failed: true)));
        Assert.False(SenkushaStates.WaitEnds(default));

        Assert.False(SenkushaStates.FailureFlagIsRead);

        if (Source() is not { } source)
            return;

        Assert.True(
            SenkushaStatesSource.ThePredicateStillReadsTwoFields(source),
            "the predicate reads state_failed now, so this port ends waits the C does not");
    }

    /// <summary>
    /// AND EVERY ENTRY STILL CLEARS BOTH FLAGS, which is the C's triple.
    ///
    /// PP773 found the stream connection's port carrying two thirds of it - the flags without the
    /// state - and the cost was a dispatch deciding by a state nobody had written. Here the check
    /// points at the C, so a port of the run has a rule to be held to rather than a habit.
    /// </summary>
    [Fact]
    public void EveryStateEntryClearsTheFlagsBeneathIt()
    {
        var entered = new SenkushaWaitState(Finished: true, Failed: true, ShouldStop: true);
        SenkushaWaitState after = SenkushaStates.Entering(entered);

        Assert.False(after.Finished);
        Assert.False(after.Failed);

        // And the stop is NOT cleared: it is the session's, not the state's.
        Assert.True(after.ShouldStop);

        if (Source() is not { } source)
            return;

        output.WriteLine($"{SenkushaStatesSource.EntryCount(source)} state entries");

        Assert.True(
            SenkushaStatesSource.EveryEntryStillClearsBothFlags(source),
            "a state entry no longer clears both flags, so it begins with the last one's answer");

        // Eight assignments for eight reachable states - and two of them are the pong, entered by
        // the RTT test and by the outbound MTU test, so the count is nine.
        Assert.Equal(9, SenkushaStatesSource.EntryCount(source));
    }

    /// <summary>
    /// FOUR TIMEOUTS FOR SIX WAITS, and two of the waits compute theirs.
    ///
    /// The connect gets thirty seconds and nothing else does; three states get five; a pong gets
    /// one. The two MTU waits derive theirs from the round trip, which is PP789's subject - here
    /// only as the fact that they use no constant.
    /// </summary>
    [Fact]
    public void TheTimeoutsAreTheFilesFourAndTwoAreDerived()
    {
        Assert.Equal(30000, SenkushaStates.TimeoutOf(SenkushaState.TakionConnect));
        Assert.Equal(5000, SenkushaStates.TimeoutOf(SenkushaState.ExpectBang));
        Assert.Equal(5000, SenkushaStates.TimeoutOf(SenkushaState.ExpectDataAck));
        Assert.Equal(1000, SenkushaStates.TimeoutOf(SenkushaState.ExpectPong));

        // Idle waits for nothing and the unreachable state has no wait to give one.
        Assert.Null(SenkushaStates.TimeoutOf(SenkushaState.Idle));
        Assert.Null(SenkushaStates.TimeoutOf(SenkushaStates.Unreachable));
        Assert.Null(SenkushaStates.TimeoutOf(SenkushaState.ExpectMtu));

        Assert.True(SenkushaStates.DerivesItsTimeout(SenkushaState.ExpectMtu));
        Assert.False(SenkushaStates.DerivesItsTimeout(SenkushaState.ExpectBang));

        if (Source() is not { } source)
            return;

        Assert.Contains($"#define CONNECT_TIMEOUT_MS {SenkushaStates.ConnectTimeoutMs}", source, StringComparison.Ordinal);
        Assert.Contains($"#define EXPECT_TIMEOUT_MS {SenkushaStates.ExpectTimeoutMs}", source, StringComparison.Ordinal);
        Assert.Contains($"#define EXPECT_PONG_TIMEOUT_MS {SenkushaStates.ExpectPongTimeoutMs}", source, StringComparison.Ordinal);
        Assert.Contains($"#define SENKUSHA_PORT {SenkushaStates.Port}", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// ITS TAKION IS NOT THE STREAM'S, which is why the port of this run cannot borrow that one.
    ///
    /// Protocol version seven rather than nine, and crypt DISABLED - so senkusha sends v7 AV
    /// packets and spends no key position at all. PP679 gave the v7 header formatter an owner for
    /// exactly this reason, and PP702 counts it among the five senkusha calls.
    /// </summary>
    [Fact]
    public void ItsTakionIsUnencryptedAndOnProtocolSeven()
    {
        Assert.Equal(7, SenkushaStates.ProtocolVersion);
        Assert.False(SenkushaStates.EncryptsItsTakion);

        if (Source() is not { } source)
            return;

        string code = CCall.Code(source);

        Assert.Contains($"takion_info.protocol_version = {SenkushaStates.ProtocolVersion};", code, StringComparison.Ordinal);
        Assert.Contains("takion_info.enable_crypt = false;", code, StringComparison.Ordinal);
    }
}

using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP365: a flag written eighteen times across two files, read nowhere, and signalled anyway.
///
/// The port reproduces it as dead. Watching it would end a failed stream at once - better, and
/// different behaviour; deleting it would make the C's log line honest but is a redesign. Either would
/// give the port a timing no message-level comparison against the C would show, so what is asserted
/// here is that it stays dead.
/// </summary>
public class StateFailedFlagTests
{
    /// <summary>
    /// THE PREDICATE IGNORES THE FLAG, in both directions.
    ///
    /// Set or clear, it changes nothing - which is exactly what makes the signal spent on it useless.
    /// </summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void TheFlagChangesNothingAboutTheStreamPredicate(
        bool finished, bool stop, bool disconnected)
    {
        Assert.Equal(
            StateFailedFlag.StreamPredicateHolds(finished, stop, disconnected, stateFailed: false),
            StateFailedFlag.StreamPredicateHolds(finished, stop, disconnected, stateFailed: true));
    }

    /// <summary>And the same in senkusha, whose predicate is the shorter of the two.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void TheFlagChangesNothingAboutTheSenkushaPredicate(bool finished, bool stop)
    {
        Assert.Equal(
            StateFailedFlag.SenkushaPredicateHolds(finished, stop, stateFailed: false),
            StateFailedFlag.SenkushaPredicateHolds(finished, stop, stateFailed: true));
    }

    /// <summary>
    /// THE FINDING, in one assertion: a handler that fails, sets the flag and signals achieves a
    /// thread going back to sleep.
    ///
    /// What follows is a full EXPECT_TIMEOUT_MS after the failure is already known.
    /// </summary>
    [Fact]
    public void SignallingAFailureWakesTheThreadIntoSleepingAgain()
    {
        // A bang handler that could not use what arrived: nothing finished, no stop, no disconnect.
        bool predicate = StateFailedFlag.StreamPredicateHolds(
            stateFinished: false, shouldStop: false, remoteDisconnected: false, stateFailed: true);

        Assert.False(predicate);
        Assert.Equal(
            WakeOutcome.TheThreadSleepsAgain,
            StateFailedFlag.OutcomeOfSignallingAFailure(predicate));
    }

    /// <summary>And a signal that has something the predicate watches does proceed.</summary>
    [Fact]
    public void ASignalThePredicateWatchesProceeds()
    {
        Assert.Equal(
            WakeOutcome.TheThreadProceeds,
            StateFailedFlag.OutcomeOfSignallingAFailure(
                StateFailedFlag.StreamPredicateHolds(true, false, false, false)));
    }

    /// <summary>
    /// AND THE FLAG IS STILL READ NOWHERE, in either file.
    ///
    /// Counted as mentions minus writes, because a read can be spelled a dozen ways and a write
    /// cannot. The count going non-zero means the port's model of a dead flag has stopped being true -
    /// which is a thing to know before the port grows a use the C does not have.
    /// </summary>
    [Theory]
    [InlineData(@"lib\src\streamconnection.c", 8)]
    [InlineData(@"lib\src\senkusha.c", 10)]
    public void TheFlagIsWrittenAndNeverRead(string relative, int expectedWrites)
    {
        string? path = StateFailedFlag.Locate(relative);
        if (path is null)
            return;

        string source = File.ReadAllText(path);

        Assert.Equal(expectedWrites, StateFailedFlag.WritesIn(source));
        Assert.Equal(0, StateFailedFlag.ReadsIn(source));
    }

    /// <summary>Both files are on the list, so it cannot quietly go empty.</summary>
    [Fact]
    public void BothFilesAreOnTheList()
    {
        Assert.Equal(
            [@"lib\src\streamconnection.c", @"lib\src\senkusha.c"],
            StateFailedFlag.Files);
    }

    /// <summary>And each file's predicate still leaves the flag out.</summary>
    [Theory]
    [InlineData(@"lib\src\streamconnection.c")]
    [InlineData(@"lib\src\senkusha.c")]
    public void EachPredicateStillIgnoresTheFlag(string relative)
    {
        string? path = StateFailedFlag.Locate(relative);
        if (path is null)
            return;

        Assert.True(
            StateFailedFlag.ThePredicateStillIgnoresIt(File.ReadAllText(path)),
            $"{relative}'s wait predicate now reads state_failed, so the port's dead flag is a live one in the C");
    }

    /// <summary>
    /// And the two handlers still spend a signal on it, which is the part that is a defect rather than
    /// dead code.
    ///
    /// If the signal goes away, what is left is ordinary dead code and this task's reasoning stops
    /// describing the file.
    /// </summary>
    [Fact]
    public void TheFailurePathsStillSpendASignalOnIt()
    {
        string? path = StateFailedFlag.Locate(@"lib\src\streamconnection.c");
        if (path is null)
            return;

        Assert.True(
            StateFailedFlag.TheFailurePathsStillSignal(
                File.ReadAllText(path),
                "static void stream_connection_takion_data_expect_bang(",
                "static void stream_connection_takion_data_expect_streaminfo("),
            "a failure path no longer signals after setting the flag, so PP365 describes something else now");
    }

    /// <summary>And the readers answer no to a file that has none of this.</summary>
    [Fact]
    public void TheReadersAnswerNoToAnEmptyFile()
    {
        Assert.False(StateFailedFlag.ThePredicateStillIgnoresIt(string.Empty));
        Assert.False(StateFailedFlag.TheFailurePathsStillSignal(string.Empty, "static void anything("));
        Assert.Equal(0, StateFailedFlag.WritesIn(string.Empty));
    }

    /// <summary>And a predicate that DID read the flag is found, so the check means something.</summary>
    [Fact]
    public void TheReaderFindsAPredicateThatReadsTheFlag()
    {
        const string wouldBeBetter = """
            static bool state_finished_cond_check(void *user)
            {
            	ChiakiStreamConnection *stream_connection = user;
            	return stream_connection->state_finished || stream_connection->state_failed || stream_connection->should_stop;
            }
            """;

        Assert.False(StateFailedFlag.ThePredicateStillIgnoresIt(wouldBeBetter));

        // And it counts as a read, which is the other half of the same fact.
        Assert.Equal(1, StateFailedFlag.ReadsIn(wouldBeBetter));
        Assert.Equal(0, StateFailedFlag.WritesIn(wouldBeBetter));
    }
}

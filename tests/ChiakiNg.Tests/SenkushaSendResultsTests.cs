using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP379, under PP295: senkusha.c discards no answer, and its disconnect says when it failed.
///
/// The rule is over every answering call rather than over the one that was wrong, which is what
/// PP370 established for the file next door. This one adds the check PP370's file does not need:
/// that the LIST is what the file actually holds, so an eleventh function is not silently outside
/// the rule.
/// </summary>
public class SenkushaSendResultsTests
{
    private static string? Core()
    {
        string? path = SenkushaSendResults.Locate();
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>THE TASK. No call in the file has its answer thrown away.</summary>
    [Fact]
    public void NoAnswerIsDiscarded()
    {
        if (Core() is not { } core)
            return;

        IReadOnlyList<string> discarded = SenkushaSendResults.DiscardedResults(core);

        Assert.True(
            discarded.Count == 0,
            "these are called for their side effect and answer something: "
            + string.Join(", ", discarded));
    }

    /// <summary>
    /// And the list covers the file, so the rule above is about all of it.
    ///
    /// PP271's lesson in the form this file needs: a rule over a list passes when the list is
    /// short, and nothing in the rule itself says how short.
    /// </summary>
    [Fact]
    public void EveryAnsweringStaticIsListed()
    {
        if (Core() is not { } core)
            return;

        IReadOnlyList<string> inTheFile = SenkushaSendResults.AnsweringStaticsIn(core);

        Assert.NotEmpty(inTheFile);

        IEnumerable<string> missing = inTheFile.Except(SenkushaSendResults.CallsThatAnswer, StringComparer.Ordinal);

        Assert.True(
            !missing.Any(),
            "these answer a ChiakiErrorCode and are not in the list the rule reads: "
            + string.Join(", ", missing));
    }

    /// <summary>THE FIX. The disconnect's answer is read and logged.</summary>
    [Fact]
    public void TheDisconnectIsReadAndLogged()
    {
        if (Core() is not { } core)
            return;

        Assert.True(
            SenkushaSendResults.TheDisconnectIsReadAndLogged(core),
            "the senkusha disconnect no longer reads and logs its answer");
    }

    /// <summary>
    /// And the run still returns what it decided, which is what makes this a report rather than a
    /// behaviour change.
    ///
    /// A teardown that overwrote `err` would turn a successful senkusha run into a failed one
    /// because its goodbye did not send - and senkusha's result is what the session's MTU and RTT
    /// come from.
    /// </summary>
    [Fact]
    public void TheRunStillReturnsWhatItDecided()
    {
        if (Core() is not { } core)
            return;

        Assert.True(
            SenkushaSendResults.TheRunStillReturnsWhatItDecided(core),
            "the senkusha teardown now overwrites the run's error code with the disconnect's");
    }

    /// <summary>
    /// PP370's rule still holds over its own file, through the reader both now share.
    ///
    /// Asserted here because the reader moved in this commit: a shared reader that broke would
    /// take both rules with it, and only one of them would be obviously about this task.
    /// </summary>
    [Fact]
    public void TheSharedReaderStillAnswersForTheStreamConnection()
    {
        string? path = StreamSendResults.Locate();
        if (path is null)
            return;

        Assert.Empty(StreamSendResults.DiscardedResults(File.ReadAllText(path)));
    }

    /// <summary>The reader sees the discard it was written for, and reads the file (PP272).</summary>
    [Fact]
    public void TheReadersSeeTheShapeTheyGuardAgainst()
    {
        const string AsItWas = """
            disconnect:
            	CHIAKI_LOGI(session->log, "Senkusha is disconnecting");

            	senkusha_send_disconnect(senkusha);
            	chiaki_mutex_unlock(&senkusha->state_mutex);
            """;

        Assert.Single(SenkushaSendResults.DiscardedResults(AsItWas));
        Assert.False(SenkushaSendResults.TheDisconnectIsReadAndLogged(AsItWas));

        Assert.Empty(SenkushaSendResults.DiscardedResults(""));
        Assert.False(SenkushaSendResults.TheDisconnectIsReadAndLogged(""));
        Assert.False(SenkushaSendResults.TheRunStillReturnsWhatItDecided(""));
        Assert.Empty(SenkushaSendResults.AnsweringStaticsIn(""));

        // And the overwrite, which is the edit that would make this a behaviour change.
        const string Overwriting = """
            	ChiakiErrorCode disconnect_err = senkusha_send_disconnect(senkusha);
            	err = disconnect_err;
            	return err;
            """;

        Assert.False(SenkushaSendResults.TheRunStillReturnsWhatItDecided(Overwriting));
    }
}

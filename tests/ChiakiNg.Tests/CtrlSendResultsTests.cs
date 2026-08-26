using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP383, under PP294: the feature burst reports, and no send in ctrl.c is thrown away.
///
/// PP342 modelled which seven messages the burst sends and in what order. What nothing modelled was
/// what happens when one does not go - and because ctrl_message_send spends the encryption counter
/// before the socket sees anything, the answer was that the channel quietly stopped working a few
/// messages later.
/// </summary>
public class CtrlSendResultsTests(ITestOutputHelper output)
{
    private static string? Ctrl() =>
        CtrlSendResults.Locate() is { } path ? File.ReadAllText(path) : null;

    private static string? Header() =>
        ChiakiNg.Session.SanitizerSource.LocateRelative(@"lib\include\chiaki\ctrl.h") is { } path
            ? File.ReadAllText(path)
            : null;

    private static string? Session() =>
        ChiakiNg.Session.SanitizerSource.LocateRelative(@"lib\src\session.c") is { } path ? File.ReadAllText(path) : null;

    /// <summary>THE SYMPTOM. The burst can report a failure at all.</summary>
    [Fact]
    public void TheBurstReturnsACode()
    {
        if (Header() is not { } header)
            return;

        Assert.True(
            CtrlSendResults.TheBurstCanReportAFailure(header),
            "ctrl_enable_features still returns void, so seven sends answer nobody");
    }

    /// <summary>And every send inside it is read.</summary>
    [Fact]
    public void NoSendInTheBurstIsUnchecked()
    {
        if (Ctrl() is not { } core)
            return;

        int unchecked_ = CtrlSendResults.UncheckedSendsInTheBurst(core);

        Assert.True(unchecked_ >= 0, "ctrl_enable_features could not be found at all");
        Assert.Equal(0, unchecked_);
    }

    /// <summary>
    /// THE HALF A LOG WOULD NOT BUY. The burst stops at the first failure rather than sending on.
    ///
    /// Every further send spends another counter value into a gap the console does not know about,
    /// so continuing widens the break. A guard that only logged would satisfy "the result is read"
    /// and do exactly that.
    /// </summary>
    [Fact]
    public void TheBurstStopsRatherThanCarryingOn()
    {
        if (Ctrl() is not { } core)
            return;

        Assert.True(
            CtrlSendResults.TheBurstStopsAtTheFirstFailure(core),
            "the burst's guard no longer returns, so a failed send is followed by six more");
    }

    /// <summary>Both callers end the channel, each by the mechanism its file has.</summary>
    [Fact]
    public void BothCallersEndTheChannel()
    {
        if (Ctrl() is { } core)
        {
            Assert.True(
                CtrlSendResults.TheHandlerEndsTheChannel(core),
                "the SESSION_ID arm no longer fails the channel on a failed burst");
        }

        if (Session() is { } session)
        {
            Assert.True(
                CtrlSendResults.TheSessionThreadEndsTheChannel(session),
                "the session thread no longer jumps to ctrl_failed on a failed burst");
        }
    }

    /// <summary>
    /// THE RULE, over the whole file rather than the function that was wrong - as a RATCHET.
    ///
    /// PP370 for streamconnection.c and PP379 for senkusha.c could both assert zero. ctrl.c cannot
    /// yet: stating the rule over the file is what found seven more discards outside the burst, and
    /// each is a different decision rather than this one repeated. They are PP385's.
    ///
    /// A ceiling rather than a narrowed rule, because narrowing it to the function this task fixed
    /// would be a check pointed away from what it found. An eighth discard is red today.
    /// </summary>
    [Fact]
    public void NoNewSendInTheFileIsDiscarded()
    {
        if (Ctrl() is not { } core)
            return;

        IReadOnlyList<string> discarded = CtrlSendResults.DiscardedResults(core);

        foreach (string call in discarded)
            output.WriteLine(call);

        Assert.True(
            discarded.Count <= CtrlSendResults.DiscardCeiling,
            $"{discarded.Count} discarded results, over the ceiling of "
            + $"{CtrlSendResults.DiscardCeiling}: " + string.Join(", ", discarded));

        // And it may fall: a ratchet left loose has given the gain away, which is the rule
        // AssertionRatchetTests states for shipped tasks and the same one applies here.
        Assert.True(
            discarded.Count == CtrlSendResults.DiscardCeiling,
            $"only {discarded.Count} discards remain - lower DiscardCeiling to that in this commit");
    }

    /// <summary>
    /// The burst is still the seven PP342 named, so the rule above is about the whole of it.
    /// </summary>
    [Fact]
    public void TheBurstIsStillSevenMessages()
    {
        Assert.Equal(7, CtrlSendResults.Burst.Count);

        // Two microphone toggles, which PP342 asserts stay two - the capture has them 108
        // microseconds apart, and two sends means two counter values.
        Assert.Equal(2, CtrlSendResults.Burst.Count(m => m == "ctrl_message_toggle_microphone"));

        if (Ctrl() is not { } core)
            return;

        string? body = ChiakiNg.Session.CFunction.Body(core, "ChiakiErrorCode ctrl_enable_features(");
        Assert.NotNull(body);

        foreach (string message in CtrlSendResults.Burst.Distinct())
            Assert.Contains(message, body, StringComparison.Ordinal);
    }

    /// <summary>The readers see the shapes they were written for, and read the file (PP272).</summary>
    [Fact]
    public void TheReadersSeeTheShapesTheyGuardAgainst()
    {
        Assert.False(CtrlSendResults.TheBurstCanReportAFailure("CHIAKI_EXPORT void ctrl_enable_features(ChiakiCtrl *ctrl);"));
        Assert.False(CtrlSendResults.TheBurstCanReportAFailure(""));

        Assert.False(CtrlSendResults.TheBurstStopsAtTheFirstFailure(""));
        Assert.False(CtrlSendResults.TheHandlerEndsTheChannel(""));
        Assert.False(CtrlSendResults.TheSessionThreadEndsTheChannel(""));

        // A guard that logs and carries on, which is the half-fix.
        const string LogsOnly = """
            #define CTRL_FEATURE_SEND(call, what) do { \
            		ChiakiErrorCode feature_err = (call); \
            		if(feature_err != CHIAKI_ERR_SUCCESS) \
            			CHIAKI_LOGE(ctrl->session->log, "failed %s", (what)); \
            	} while(0)
            """;

        Assert.False(CtrlSendResults.TheBurstStopsAtTheFirstFailure(LogsOnly));

        // And a bare send is still found by the file-wide reader.
        const string Bare = "\tctrl_message_send(ctrl, CTRL_MESSAGE_TYPE_GO_HOME, NULL, 0);";

        Assert.Single(CtrlSendResults.DiscardedResults(Bare));
    }
}

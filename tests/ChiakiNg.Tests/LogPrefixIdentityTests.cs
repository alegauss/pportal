using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP390, under PP340: holepunch.c's misattributed logs are exactly the ones already decided.
///
/// PP389 swept this file, found seven logs wearing another function's name, and fixed them. All
/// seven were deliberate - PP235 reproduces two on purpose so this port's logs match reports
/// written against the Qt client's, and PP238 ruled five defensible because the prefix names the
/// OPERATION a reader is following across a call tree. PP389 was retired; it was the fourth
/// rediscovery after PP237 and PP256, and the only thing that caught it was an unrelated check
/// going red.
///
/// So the rule here is not that no log wears another name. It is that the set which does is exactly
/// what those two decisions declare - and, because a list can outlive what it describes, that each
/// name in them still wears one.
/// </summary>
public class LogPrefixIdentityTests(ITestOutputHelper output)
{
    private static string? Core() =>
        LogPrefixIdentity.Locate() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// THE TASK. Every log wearing another function's name is one PP235 or PP238 already decided.
    ///
    /// NOT "no log wears another name" - that is what PP389 asserted, and it was wrong. Seven do,
    /// all seven deliberately, and a rule demanding zero would have to be satisfied by undoing two
    /// decisions. What this asks is whether the file is still what those decisions describe.
    /// </summary>
    [Fact]
    public void EveryMisattributedLogIsOneAlreadyDecided()
    {
        if (Core() is not { } core)
            return;

        IReadOnlyList<PrefixedLog> attributions = LogPrefixIdentity.AttributionsIn(core);

        // PP271: the sweep found the convention, or the rule below is about nothing. Two hundred
        // and more today - the rest of the file's prefixed messages name something that is not a
        // function here and claim nothing.
        Assert.True(attributions.Count >= 200, $"only {attributions.Count} attributions were found");
        output.WriteLine($"{attributions.Count} logs prefixed with a function name in this file");

        foreach (PrefixedLog log in LogPrefixIdentity.WearingAnothersName(attributions))
            output.WriteLine(log.ToString());

        IReadOnlyList<PrefixedLog> unaccounted = LogPrefixIdentity.UnaccountedFor(attributions);

        Assert.True(
            unaccounted.Count == 0,
            "these logs open with another function's name and neither PP235 nor PP238 accounts for "
            + "them - which is a question about whether it was decided, not a fix:\n  "
            + string.Join("\n  ", unaccounted));
    }

    /// <summary>
    /// And the seven really are there, so the rule above is forgiving something rather than finding
    /// nothing.
    ///
    /// This is what PP389 needed and did not have. A sweep that reported zero misattributions and
    /// one that reported seven already-decided ones read identically without it.
    /// </summary>
    [Fact]
    public void TheSevenDecidedOnesAreStillThere()
    {
        if (Core() is not { } core)
            return;

        IReadOnlyList<PrefixedLog> wearing =
            LogPrefixIdentity.WearingAnothersName(LogPrefixIdentity.AttributionsIn(core));

        Assert.Equal(7, wearing.Count);

        // Two in deleteSession, reproduced on purpose by PP235.
        Assert.Equal(2, wearing.Count(l => l.In == "deleteSession"));

        // And five across check_candidates' three helpers, ruled defensible by PP238.
        Assert.Equal(
            5,
            wearing.Count(l => MisnamedLogs.NamesTheOperationNotTheFunction.Contains(l.In, StringComparer.Ordinal)));
    }

    /// <summary>
    /// The lists are held to the file too: a function they say wears another name, and no longer
    /// does, is a decision that has outlived what it described.
    /// </summary>
    [Fact]
    public void NoListEntryOutlivesTheLineItDescribes()
    {
        if (Core() is not { } core)
            return;

        IReadOnlyList<string> stale = LogPrefixIdentity.DeclaredButNoLongerWearingAnothersName(
            LogPrefixIdentity.AttributionsIn(core));

        Assert.True(
            stale.Count == 0,
            "PP238's list names these as logging another function's name, and they no longer do: "
            + string.Join(", ", stale));
    }

    /// <summary>
    /// THE COST PP235 ACCEPTED, measured rather than described.
    ///
    /// One name plus one sentence reachable from two functions is a grep that finds both and cannot
    /// choose. deleteSession's two are exactly that - verbatim copies of lines still standing in
    /// http_send_session_message. PP235 decided the alternative was worse, so this asserts the cost
    /// EXISTS at the size it was accepted at, rather than asserting it away.
    /// </summary>
    [Fact]
    public void TheAcceptedCostIsStillTwoSentences()
    {
        if (Core() is not { } core)
            return;

        IReadOnlyList<string> ungreppable =
            LogPrefixIdentity.MessagesUnderTwoNames(LogPrefixIdentity.AttributionsIn(core));

        foreach (string message in ungreppable)
            output.WriteLine(message);

        // THREE, not two. deleteSession's pair are PP235's, and a third belongs to PP238's group:
        // "check_candidates: Received response of unexpected type" stands in check_candidates AND
        // in receive_request_send_response_ps, under the one name. So the operation-prefix rule has
        // an ungreppable line of its own, which neither decision wrote down.
        Assert.Equal(3, ungreppable.Count);

        Assert.Equal(
            2, ungreppable.Count(m => m.StartsWith("http_send_session_message:", StringComparison.Ordinal)));
        Assert.Equal(
            1, ungreppable.Count(m => m.StartsWith("check_candidates:", StringComparison.Ordinal)));
    }

    /// <summary>
    /// And a shared sentence under two DIFFERENT correct names is left alone, which is the case
    /// that would have made a file-wide version of that rule useless.
    ///
    /// Twelve CURL setopt failures say the same words in a dozen functions. Each names its own, so
    /// each is distinguishable - the convention working, not a finding.
    /// </summary>
    [Fact]
    public void ASharedSentenceUnderTwoCorrectNamesIsFine()
    {
        if (Core() is not { } core)
            return;

        IReadOnlyList<string> shared =
            LogPrefixIdentity.MessagesUnderTwoNames(LogPrefixIdentity.AttributionsIn(core));

        // Not one of the three is a CURL setopt line, though a dozen of those share a sentence -
        // each names its own function, so each is distinguishable. That is the rule discriminating
        // rather than the file being uniform.
        Assert.DoesNotContain(shared, m => m.Contains("CURL setopt", StringComparison.Ordinal));

        IReadOnlyList<PrefixedLog> setopt =
            [.. LogPrefixIdentity.AttributionsIn(core)
                .Where(l => l.Message.StartsWith("CURL setopt", StringComparison.Ordinal))];

        Assert.True(setopt.Count > 5, $"only {setopt.Count} CURL setopt logs were found");
        Assert.True(
            setopt.Select(l => l.In).Distinct(StringComparer.Ordinal).Count() > 1,
            "the CURL setopt logs are all in one function, so they prove nothing here");

        output.WriteLine($"{setopt.Count} CURL setopt logs across "
            + $"{setopt.Select(l => l.In).Distinct(StringComparer.Ordinal).Count()} functions");
    }

    /// <summary>
    /// The reader sees the seven as they were, so the green above is not a reader that agrees with
    /// anything it is shown.
    /// </summary>
    [Fact]
    public void TheReaderSeesTheShapeItGuardsAgainst()
    {
        const string AsItWas = """
            static ChiakiErrorCode http_send_session_message(Session *session, SessionMessage *message, bool short_msg)
            {
                CHIAKI_LOGE(session->log, "http_send_session_message: Sending holepunch session message failed with HTTP code %ld.", http_code);
            }

            static ChiakiErrorCode deleteSession(Session *session)
            {
                CHIAKI_LOGE(session->log, "http_send_session_message: Sending holepunch session message failed with HTTP code %ld.", http_code);
            }
            """;

        IReadOnlyList<PrefixedLog> attributions = LogPrefixIdentity.AttributionsIn(AsItWas);

        Assert.Equal(2, attributions.Count);

        PrefixedLog wrong = Assert.Single(LogPrefixIdentity.WearingAnothersName(attributions));
        Assert.Equal("deleteSession", wrong.In);
        Assert.Equal("http_send_session_message", wrong.Claims);

        // And the duplicate is caught by the other half too, which is what a reader grepping the
        // message would have hit.
        Assert.Single(LogPrefixIdentity.MessagesUnderTwoNames(attributions));
    }

    /// <summary>
    /// A prefix that is not a function in this file claims nothing and is left alone.
    ///
    /// Most of holepunch.c's messages are not attributions at all, and judging them would be a
    /// rule about English rather than about where a log came from.
    /// </summary>
    [Fact]
    public void APrefixThatNamesNothingIsNotJudged()
    {
        const string Source = """
            static ChiakiErrorCode f(Session *session)
            {
                CHIAKI_LOGE(session->log, "STUN: no response from any server");
                CHIAKI_LOGI(session->log, "Sent response to %s:%d", a, b);
            }
            """;

        Assert.Empty(LogPrefixIdentity.AttributionsIn(Source));
    }

    /// <summary>And it reads the file it is given (PP272).</summary>
    [Fact]
    public void TheReaderReadsTheFile()
    {
        Assert.Empty(LogPrefixIdentity.AttributionsIn(""));
        Assert.Empty(LogPrefixIdentity.FunctionsIn(""));
        Assert.Empty(LogPrefixIdentity.WearingAnothersName([]));
        Assert.Empty(LogPrefixIdentity.MessagesUnderTwoNames([]));
    }
}

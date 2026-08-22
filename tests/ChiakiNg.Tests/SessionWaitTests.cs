using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP293: the five waits, and the two conditions that end every one of them.
/// </summary>
public class SessionWaitTests
{
    public static TheoryData<SessionWaitKind> AllKinds() =>
    [
        SessionWaitKind.State,
        SessionWaitKind.CtrlStart,
        SessionWaitKind.Pin,
        SessionWaitKind.StreamConnectionSwitch,
        SessionWaitKind.Regist,
    ];

    /// <summary>
    /// A stop ends every wait, and a control failure ends every wait.
    ///
    /// This is the property the five predicates share and the reason a caller cannot read "the wait
    /// returned" as "the thing I waited for happened".
    /// </summary>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void StopAndFailureEndEveryWait(SessionWaitKind kind)
    {
        Assert.True(SessionWait.IsSatisfied(kind, new SessionState(ShouldStop: true)));
        Assert.True(SessionWait.IsSatisfied(kind, new SessionState(CtrlFailed: true)));

        Assert.Equal(SessionWaitReason.Stopped, SessionWait.Reason(kind, new SessionState(ShouldStop: true)));
        Assert.Equal(SessionWaitReason.CtrlFailed, SessionWait.Reason(kind, new SessionState(CtrlFailed: true)));
    }

    /// <summary>And an empty state ends none of them.</summary>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void NothingHappeningEndsNoWait(SessionWaitKind kind)
    {
        Assert.False(SessionWait.IsSatisfied(kind, default));
        Assert.Equal(SessionWaitReason.StillWaiting, SessionWait.Reason(kind, default));
    }

    /// <summary>
    /// Stop wins over the happy condition, which is the ordering that matters.
    ///
    /// A session asked to stop that ALSO got its session id must stop. Reading the happy condition
    /// first would carry it into a stream nobody wants, and the wait would have looked successful.
    /// </summary>
    [Fact]
    public void StopWinsOverTheThingWaitedFor()
    {
        var both = new SessionState(ShouldStop: true, CtrlSessionIdReceived: true);

        Assert.True(SessionWait.IsSatisfied(SessionWaitKind.CtrlStart, both));
        Assert.Equal(SessionWaitReason.Stopped, SessionWait.Reason(SessionWaitKind.CtrlStart, both));
    }

    /// <summary>...and a control failure wins over it too.</summary>
    [Fact]
    public void FailureWinsOverTheThingWaitedFor()
    {
        var both = new SessionState(CtrlFailed: true, LoginPinEntered: true);
        Assert.Equal(SessionWaitReason.CtrlFailed, SessionWait.Reason(SessionWaitKind.Pin, both));
    }

    /// <summary>Each wait ends on its own condition and no other wait's.</summary>
    [Theory]
    [InlineData(SessionWaitKind.CtrlStart)]
    [InlineData(SessionWaitKind.Pin)]
    [InlineData(SessionWaitKind.StreamConnectionSwitch)]
    [InlineData(SessionWaitKind.Regist)]
    public void EachWaitIsEndedOnlyByItsOwnCondition(SessionWaitKind kind)
    {
        SessionState[] others =
        [
            new SessionState(CtrlSessionIdReceived: true),
            new SessionState(CtrlLoginPinRequested: true),
            new SessionState(LoginPinEntered: true),
            new SessionState(StreamConnectionSwitchReceived: true),
            new SessionState(PsnRegistSucceeded: true),
        ];

        int satisfied = others.Count(s => SessionWait.Specific(kind, s));

        // CtrlStart is the one with two conditions - a session id OR a PIN request - and the rest
        // have exactly one.
        Assert.Equal(kind == SessionWaitKind.CtrlStart ? 2 : 1, satisfied);
    }

    /// <summary>
    /// The plain state wait has no condition of its own, which is not an omission.
    ///
    /// session_check_state_pred is should_stop or ctrl_failed and nothing else: it is the wait for
    /// "something went wrong", used where the thread has nothing to look forward to.
    /// </summary>
    [Fact]
    public void ThePlainStateWaitHasNoConditionOfItsOwn()
    {
        Assert.False(SessionWait.Specific(SessionWaitKind.State,
            new SessionState(CtrlSessionIdReceived: true, LoginPinEntered: true,
                StreamConnectionSwitchReceived: true, PsnRegistSucceeded: true)));
    }

    /// <summary>THE DRIFT CHECK. The C's five predicates still share the same two disjuncts.</summary>
    [Fact]
    public void TheCStillSharesTheTwo()
    {
        string? file = SanitizerSource.LocateRelative(SessionCoreSource.RelativePath);
        Assert.True(file is not null, "no lib\\src\\session.c - this file is describing nothing");

        string core = File.ReadAllText(file);

        string[] predicates =
        [
            "session_check_state_pred",
            "session_check_state_pred_ctrl_start",
            "session_check_state_pred_pin",
            "session_check_state_pred_stream_connection_switch",
            "session_check_state_pred_regist",
        ];

        // Each predicate's own body, not the whole file. Written the other way first and it counted
        // ten should_stop - five predicates and five assignments elsewhere, which is a count that
        // agrees with the claim by accident and would keep agreeing after a predicate lost one.
        foreach (string predicate in predicates)
        {
            string body = BodyOf(core, predicate);
            Assert.False(body.Length == 0, $"{predicate} is gone");

            Assert.Contains("session->should_stop", body, StringComparison.Ordinal);
            Assert.Contains("session->ctrl_failed", body, StringComparison.Ordinal);
        }
    }

    /// <summary>One function's body, from its signature to the closing brace at column zero.</summary>
    private static string BodyOf(string core, string name)
    {
        int at = core.IndexOf($"static bool {name}(void *user)", StringComparison.Ordinal);
        if (at < 0)
            return "";

        int end = core.IndexOf("\n}", at, StringComparison.Ordinal);
        return end < 0 ? core[at..] : core[at..end];
    }
}

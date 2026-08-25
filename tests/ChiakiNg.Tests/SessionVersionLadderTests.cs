using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP334, continuing PP293: the RP-Version retry, which is three attempts written as two ifs and a
/// bare check.
///
/// The corpus cannot judge any of this. PP297's capture is of a session that connected on the first
/// request, so every rung below the first exists in session.c and in no recording - which is why it
/// is asserted against the source, and why the source assertions are as much of this file as the
/// behavioural ones.
/// </summary>
public class SessionVersionLadderTests
{
    private const string Ours = "10.0";

    private static SessionResponseFields Refused(RpApplicationReason reason, string? version = null)
        => new(Success: false, Nonce: null, RpVersion: version, ErrorCode: (uint)reason);

    /// <summary>A granted request with a nonce is granted, and asks nothing again.</summary>
    [Fact]
    public void AGrantedRequestEndsTheLadder()
    {
        AttemptResult result = SessionVersionLadder.Read(
            new SessionResponseFields(true, "bm9uY2U=", Ours, 0), Ours, ps5: true, canRetarget: true);

        Assert.Equal(AttemptOutcome.Granted, result.Outcome);
        Assert.False(SessionVersionLadder.AsksAgain(result, 1));
    }

    /// <summary>
    /// Success with no nonce is NOT success.
    ///
    /// session.c base64-decodes the nonce after deciding the response succeeded, and fails the
    /// request where it is absent or not sixteen bytes - so a console that answers 200 and sends
    /// none ends the session rather than starting crypto from nothing.
    /// </summary>
    [Fact]
    public void SuccessWithoutANonceIsARefusal()
    {
        AttemptResult result = SessionVersionLadder.Read(
            new SessionResponseFields(true, null, Ours, 0), Ours, ps5: true, canRetarget: true);

        Assert.Equal(AttemptOutcome.Refused, result.Outcome);
        Assert.Equal(ChiakiQuitReason.SessionRequestUnknown, result.QuitReason);
    }

    /// <summary>
    /// A version mismatch naming a version we can parse retargets, and the ladder asks again.
    /// </summary>
    [Fact]
    public void AVersionWeCanParseIsRetargeted()
    {
        AttemptResult result = SessionVersionLadder.Read(
            Refused(RpApplicationReason.RpVersion, "9.0"), Ours, ps5: false, canRetarget: true);

        Assert.Equal(AttemptOutcome.VersionMismatch, result.Outcome);
        Assert.Equal(ChiakiTarget.Ps4_9, result.NextTarget);
        Assert.True(SessionVersionLadder.AsksAgain(result, 1));
    }

    /// <summary>
    /// THE VERSIONS HAVE TO ACTUALLY DIFFER.
    ///
    /// The mismatch branch is guarded by a strcmp, and it is the guard a port drops by accident: a
    /// console reporting the version we already sent is not a mismatch, it is a refusal. Without
    /// this the ladder would send the same request three times and call the third one different.
    /// </summary>
    [Fact]
    public void TheSameVersionBackIsARefusalAndNotAMismatch()
    {
        AttemptResult result = SessionVersionLadder.Read(
            Refused(RpApplicationReason.RpVersion, Ours), Ours, ps5: true, canRetarget: true);

        Assert.Equal(ChiakiTarget.Ps4Unknown, result.NextTarget);
        Assert.False(SessionVersionLadder.AsksAgain(result, 1));
    }

    /// <summary>
    /// 5.0 IS NONSENSE AND IS ANSWERED WITH 9.0, which is upstream's guess and is kept as one.
    /// </summary>
    [Fact]
    public void FiveIsTreatedAsNonsenseAndAnsweredWithNine()
    {
        AttemptResult result = SessionVersionLadder.Read(
            Refused(RpApplicationReason.RpVersion, SessionVersionLadder.NonsenseVersion),
            Ours, ps5: false, canRetarget: true);

        Assert.Equal(ChiakiTarget.Ps4_9, result.NextTarget);
        Assert.True(SessionVersionLadder.AsksAgain(result, 1));
    }

    /// <summary>
    /// A version nothing parses, and not 5.0, is a mismatch with nowhere to go - so the ladder
    /// stops on its own guard rather than asking again with an unknown target.
    /// </summary>
    [Fact]
    public void AVersionNothingParsesStopsTheLadder()
    {
        AttemptResult result = SessionVersionLadder.Read(
            Refused(RpApplicationReason.RpVersion, "99.7"), Ours, ps5: false, canRetarget: true);

        Assert.Equal(AttemptOutcome.VersionMismatch, result.Outcome);
        Assert.True(RpVersion.IsUnknown(result.NextTarget));
        Assert.False(SessionVersionLadder.AsksAgain(result, 1));
        Assert.Equal(ChiakiQuitReason.SessionRequestRpVersionMismatch, result.QuitReason);
    }

    /// <summary>
    /// THE THIRD ATTEMPT CANNOT RE-DETECT, and the same answer therefore lands differently.
    ///
    /// session.c passes target_out on the first two attempts and NULL on the third, and the branch
    /// that works out a new target tests target_out - so an identical reply retargets on attempt two
    /// and falls to the refusal switch on attempt three. That is not a tidiness detail: it is the
    /// only thing making this three attempts rather than a loop against a console reporting nonsense.
    /// </summary>
    [Fact]
    public void TheSameReplyRetargetsOnTheSecondAttemptAndEndsItOnTheThird()
    {
        SessionResponseFields reply = Refused(RpApplicationReason.RpVersion, "9.0");

        AttemptResult second = SessionVersionLadder.Read(reply, Ours, ps5: false, canRetarget: true);
        AttemptResult third = SessionVersionLadder.Read(reply, Ours, ps5: false, canRetarget: false);

        Assert.Equal(ChiakiTarget.Ps4_9, second.NextTarget);
        Assert.True(SessionVersionLadder.AsksAgain(second, 2));

        Assert.Equal(ChiakiTarget.Ps4Unknown, third.NextTarget);
        Assert.False(SessionVersionLadder.AsksAgain(third, 3));
        Assert.Equal(ChiakiQuitReason.SessionRequestRpVersionMismatch, third.QuitReason);
    }

    /// <summary>
    /// An UNKNOWN reason retargets while a target may be worked out, and is a plain refusal once
    /// one may not - which is the second place the third attempt reads differently.
    /// </summary>
    [Fact]
    public void AnUnknownReasonRetargetsOnlyWhileItMay()
    {
        SessionResponseFields reply = Refused(RpApplicationReason.Unknown, "9.0");

        Assert.Equal(
            ChiakiTarget.Ps4_9,
            SessionVersionLadder.Read(reply, Ours, ps5: false, canRetarget: true).NextTarget);

        AttemptResult last = SessionVersionLadder.Read(reply, Ours, ps5: false, canRetarget: false);
        Assert.Equal(AttemptOutcome.Refused, last.Outcome);
        Assert.Equal(ChiakiQuitReason.SessionRequestUnknown, last.QuitReason);
    }

    /// <summary>The refusal switch, reason by reason.</summary>
    [Theory]
    [InlineData(RpApplicationReason.InUse, ChiakiQuitReason.SessionRequestRpInUse)]
    [InlineData(RpApplicationReason.Crash, ChiakiQuitReason.SessionRequestRpCrash)]
    [InlineData(RpApplicationReason.RegistFailed, ChiakiQuitReason.SessionRequestUnknown)]
    [InlineData(RpApplicationReason.InvalidPsnId, ChiakiQuitReason.SessionRequestUnknown)]
    public void ARefusalCarriesTheQuitReasonItsCodeNames(
        RpApplicationReason reason, ChiakiQuitReason quit)
    {
        AttemptResult result = SessionVersionLadder.Read(
            Refused(reason), Ours, ps5: true, canRetarget: true);

        Assert.Equal(AttemptOutcome.Refused, result.Outcome);
        Assert.Equal(quit, result.QuitReason);
        Assert.False(SessionVersionLadder.AsksAgain(result, 1));
    }

    /// <summary>And the ladder never asks a fourth time, whatever the answer.</summary>
    [Fact]
    public void TheLadderNeverAsksAFourthTime()
    {
        AttemptResult retargeting = SessionVersionLadder.Read(
            Refused(RpApplicationReason.RpVersion, "9.0"), Ours, ps5: false, canRetarget: true);

        Assert.True(SessionVersionLadder.AsksAgain(retargeting, 1));
        Assert.True(SessionVersionLadder.AsksAgain(retargeting, 2));
        Assert.False(SessionVersionLadder.AsksAgain(retargeting, 3));
    }

    /// <summary>
    /// And session.c still has the ladder this reproduces: three attempts, the strcmp guard, and
    /// the 5.0 substitution.
    /// </summary>
    [Fact]
    public void SessionStillDeclaresTheLadder()
    {
        string? path = SessionLadderSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(SessionLadderSource.TheLadderIsStillThreeAttempts(core), "the retry ladder has changed");
        Assert.True(
            SessionLadderSource.TheMismatchStillNeedsTheVersionsToDiffer(core),
            "the strcmp guard on the mismatch branch has gone");
        Assert.True(SessionLadderSource.FiveIsStillNonsense(core), "the 5.0 substitution has changed");
    }

    /// <summary>And the six reason codes are still the numbers session.h defines.</summary>
    [Fact]
    public void TheReasonCodesStillMatchTheHeader()
    {
        string? path = SessionLadderSource.LocateHeader();
        if (path is null)
            return;

        Assert.True(SessionLadderSource.TheReasonsAreStill(File.ReadAllText(path)));
    }
}

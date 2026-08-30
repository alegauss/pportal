using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP553: the seven asks answered from the three sequences, with no C behind them.
/// </summary>
public class SequencedHolepunchSessionTests
{
    private static HolepunchPunchResult Result(HolepunchPunchOutcome outcome)
        => new(outcome, null, []);

    private static SequencedHolepunchSession Session(
        HolepunchPunchOutcome outcome = HolepunchPunchOutcome.Punched)
        => new(_ => Task.FromResult(Result(outcome)));

    /// <summary>
    /// THE POINT OF THE CLASS: PP479's flow runs to the end over this, with no C loaded.
    ///
    /// PP481 answered the same interface by P/Invoking holepunch.c, which is what PP533 called the
    /// direction that removes nothing. This is the other one, and the evidence is that the flow
    /// PP479 wrote against session.c does not know the difference.
    /// </summary>
    [Fact]
    public void ThePsnFlowRunsOverItWithNoCLoaded()
    {
        using var session = Session();
        session.Record(HolepunchPortType.Ctrl, new object());
        session.Record(HolepunchPortType.Data, new object());
        session.RegistInfo = new object();
        session.SelectedAddress = "203.0.113.7";
        session.CtrlPort = 9295;

        HolepunchConnectOutcome outcome = new HolepunchConnect(session, socket => socket).Run();

        Assert.Null(outcome.FailedAt);
        Assert.Equal(ChiakiError.Success, outcome.Error);
        Assert.Equal("203.0.113.7", outcome.Hostname);
        Assert.Equal(9295, outcome.CtrlPort);
        Assert.NotNull(outcome.Rudp);
        Assert.NotNull(outcome.DataSocket);
    }

    /// <summary>
    /// The three the C runs before the session thread, in that order - and a failure at any of them
    /// stops the rest, because there is nothing for the next one to run on.
    /// </summary>
    [Fact]
    public async Task PrepareRunsTheThreeAndStopsAtTheFirstFailure()
    {
        var ran = new List<string>();

        Task<bool> Create(bool ok) { ran.Add("create"); return Task.FromResult(ok); }
        Task<bool> Start(bool ok) { ran.Add("start"); return Task.FromResult(ok); }
        Task<HolepunchPunchResult> Punch(HolepunchPunchOutcome how)
        {
            ran.Add("punch");
            return Task.FromResult(Result(how));
        }

        Assert.True(await SequencedHolepunchSession.PrepareAsync(
            () => Create(true), () => Start(true), () => Punch(HolepunchPunchOutcome.Punched)));
        Assert.Equal(["create", "start", "punch"], ran);

        ran.Clear();
        Assert.False(await SequencedHolepunchSession.PrepareAsync(
            () => Create(false), () => Start(true), () => Punch(HolepunchPunchOutcome.Punched)));
        Assert.Equal(["create"], ran);

        ran.Clear();
        Assert.False(await SequencedHolepunchSession.PrepareAsync(
            () => Create(true), () => Start(false), () => Punch(HolepunchPunchOutcome.Punched)));
        Assert.Equal(["create", "start"], ran);

        // And a punch that did not punch is not a prepared session, whatever else went right.
        ran.Clear();
        Assert.False(await SequencedHolepunchSession.PrepareAsync(
            () => Create(true), () => Start(true), () => Punch(HolepunchPunchOutcome.TimedOut)));
        Assert.Equal(["create", "start", "punch"], ran);
    }

    /// <summary>
    /// Each punch outcome is reported as the error a C caller would act on. A timeout is HostDown,
    /// which is PP546's reading: a console that does not answer inside the deadline is down.
    /// </summary>
    [Theory]
    [InlineData(HolepunchPunchOutcome.Punched, ChiakiError.Success)]
    [InlineData(HolepunchPunchOutcome.Cancelled, ChiakiError.Canceled)]
    [InlineData(HolepunchPunchOutcome.TimedOut, ChiakiError.HostDown)]
    [InlineData(HolepunchPunchOutcome.Uninitialised, ChiakiError.InvalidData)]
    [InlineData(HolepunchPunchOutcome.Failed, ChiakiError.Unknown)]
    public void EveryPunchOutcomeHasAnError(HolepunchPunchOutcome outcome, ChiakiError expected)
        => Assert.Equal(expected, SequencedHolepunchSession.Reported(outcome));

    /// <summary>
    /// PP554: A CANCELLED TOKEN IS Canceled, NOT AN EXCEPTION THROUGH A C STACK FRAME.
    ///
    /// The punch has a whole one-shot for answering Canceled, and a token bypassed all of it: the
    /// poll's Task.Delay throws, nothing on the way out catches, and PunchHole rethrew it from a
    /// method whose contract is an error code - to a caller that cannot catch anything.
    /// </summary>
    [Fact]
    public void ACancelledTokenIsAnErrorCodeNotAThrow()
    {
        using var session = new SequencedHolepunchSession(
            _ => Task.FromCanceled<HolepunchPunchResult>(new CancellationToken(canceled: true)));

        Assert.Equal(ChiakiError.Canceled, session.PunchHole(HolepunchPortType.Data));
        Assert.Null(session.Thrown);
    }

    /// <summary>
    /// And so is anything else the sequence throws - kept, so a managed caller can still find out
    /// what happened, while the C gets the only thing it can read.
    /// </summary>
    [Fact]
    public void AnythingElseThrownBecomesUnknownAndIsKept()
    {
        var boom = new InvalidOperationException("the socket went away");
        using var session = new SequencedHolepunchSession(
            _ => Task.FromException<HolepunchPunchResult>(boom));

        Assert.Equal(ChiakiError.Unknown, session.PunchHole(HolepunchPortType.Ctrl));
        Assert.Same(boom, session.Thrown);
    }

    /// <summary>
    /// The flow survives it too, which is the point: PP479 quits at the step that failed rather
    /// than unwinding through it.
    /// </summary>
    [Fact]
    public void TheFlowQuitsRatherThanUnwinding()
    {
        using var session = new SequencedHolepunchSession(
            _ => Task.FromCanceled<HolepunchPunchResult>(new CancellationToken(canceled: true)));
        session.Record(HolepunchPortType.Ctrl, new object());
        session.RegistInfo = new object();

        HolepunchConnectOutcome outcome = new HolepunchConnect(session, socket => socket).Run();

        Assert.Equal(ChiakiError.Canceled, outcome.Error);
    }

    /// <summary>A punch that fails is reported to the flow, which quits at that step.</summary>
    [Fact]
    public void AFailedPunchStopsTheFlow()
    {
        using var session = Session(HolepunchPunchOutcome.TimedOut);
        session.Record(HolepunchPortType.Ctrl, new object());
        session.RegistInfo = new object();

        HolepunchConnectOutcome outcome = new HolepunchConnect(session, socket => socket).Run();

        Assert.Equal(ChiakiError.HostDown, outcome.Error);
        Assert.Null(outcome.DataSocket);
    }

    /// <summary>
    /// PP551: the registration info is not manufactured here. A class that made and kept one would
    /// be writing the field PP479 warns about - the caller owns it for as long as the registration
    /// takes.
    /// </summary>
    [Fact]
    public void TheRegistrationInfoIsGivenNotMade()
    {
        using var session = Session();

        Assert.Throws<InvalidOperationException>(session.GetRegistInfo);

        object given = new();
        session.RegistInfo = given;
        Assert.Same(given, session.GetRegistInfo());
    }

    /// <summary>A socket is asked for by port, and asking before a punch is an error not a null.</summary>
    [Fact]
    public void ASocketIsAskedForByPort()
    {
        using var session = Session();
        object ctrl = new();
        session.Record(HolepunchPortType.Ctrl, ctrl);

        Assert.Same(ctrl, session.GetSocket(HolepunchPortType.Ctrl));
        Assert.Throws<InvalidOperationException>(() => session.GetSocket(HolepunchPortType.Data));
    }

    /// <summary>
    /// The offer is recorded, not sent - PP550's punch sends it as one of its eleven steps, so a
    /// send here would be the second one on the wire.
    /// </summary>
    [Fact]
    public void TheOfferIsRecordedRatherThanSent()
    {
        using var session = Session();

        Assert.False(session.OfferMade);
        Assert.Equal(ChiakiError.Success, session.CreateOffer());
        Assert.True(session.OfferMade);
    }

    /// <summary>
    /// PP555: THE OFFER'S FAILURE MOVES ONE STEP, and is not lost on the way.
    ///
    /// PP460 gives the C's CreateOffer step the guard QuitsToCtrlTeardown - a real path session.c
    /// takes when the offer is refused. Against this session that guard cannot fire, because the
    /// offer has not been sent when the call is made. It arrives as a punch failure instead, and
    /// the flag is what stops the cause being lost with it.
    /// </summary>
    [Fact]
    public void TheOffersFailureArrivesAtThePunchInstead()
    {
        // The C's guard on this step is real, which is what makes its absence here a departure.
        Assert.Equal(HolepunchGuard.QuitsToCtrlTeardown, HolepunchFlow.GuardFor(HolepunchStep.CreateOffer));

        using var session = new SequencedHolepunchSession(
            _ => Task.FromResult(new HolepunchPunchResult(
                HolepunchPunchOutcome.Failed, HolepunchPunchStep.SendOffer, [])));
        session.Record(HolepunchPortType.Ctrl, new object());
        session.RegistInfo = new object();

        HolepunchConnectOutcome outcome = new HolepunchConnect(session, socket => socket).Run();

        // A step later than the C would have stopped, and named as the offer all the same.
        Assert.Equal(SequencedHolepunchSession.TheOfferFailsAt, outcome.FailedAt);
        Assert.NotEqual(HolepunchStep.CreateOffer, outcome.FailedAt);
        Assert.True(session.OfferFailed);
    }

    /// <summary>And a punch that failed at any other step is not the offer.</summary>
    [Fact]
    public void APunchThatFailedElsewhereIsNotTheOffer()
    {
        using var session = new SequencedHolepunchSession(
            _ => Task.FromResult(new HolepunchPunchResult(
                HolepunchPunchOutcome.TimedOut, HolepunchPunchStep.WaitForAccept, [])));

        Assert.Equal(ChiakiError.HostDown, session.PunchHole(HolepunchPortType.Data));
        Assert.False(session.OfferFailed);
    }

    /// <summary>The releases are counted, which is how a teardown test sees it happened once.</summary>
    [Fact]
    public void TheReleasesAreCounted()
    {
        using var session = Session();

        Assert.Equal(0, session.FinisCalled);
        session.Fini();
        Assert.Equal(1, session.FinisCalled);
    }
}
